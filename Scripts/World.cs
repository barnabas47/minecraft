using Godot;
using System;
using System.Collections.Generic;

namespace Minecraft;

public partial class World : Node3D
{
	[Export] public PackedScene ChunkScene { get; set; } = null!;
	[Export] public int RenderDistance { get; set; } = 4;

	private readonly Dictionary<Vector2I, Chunk> _chunks = new();
	private readonly FastNoiseLite _noise = new();
	private Player _player = null!;
	private Vector2I _lastPlayerChunk = new Vector2I(999, 999);
	public readonly Dictionary<Vector3I, FurnaceState> Furnaces = new();
	private readonly Queue<Vector2I> _chunkLoadQueue = new();
	private readonly System.Collections.Concurrent.ConcurrentDictionary<Vector2I, bool> _chunksLoading = new();
	private readonly System.Collections.Concurrent.ConcurrentQueue<(Chunk Chunk, Chunk.MeshData MeshData, bool TriggerNeighbors)> _meshApplyQueue = new();
	private int _frameCounter = 0;

	public override void _Ready()
	{
		// Zajgenerátor beállítása a hullámzó domborzathoz
		_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
		_noise.Seed = (int)GD.Randi(); // Különböző seed minden indításkor
		_noise.Frequency = 0.015f;    // Kisebb = tágasabb dombok, nagyobb = gyakoribb dombhullámok
		_noise.FractalOctaves = 4;

		_player = GetNode<Player>("Player");
		
		if (_player != null)
		{
			UpdateChunksAroundPlayer(_player.Position);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player != null)
		{
			Vector3 playerPos = _player.Position;
			Vector2I playerChunk = GetChunkCoord(Mathf.FloorToInt(playerPos.X), Mathf.FloorToInt(playerPos.Z));
			if (playerChunk != _lastPlayerChunk)
			{
				_lastPlayerChunk = playerChunk;
				UpdateChunksAroundPlayer(playerPos);
			}
		}

		_frameCounter++;
		if (_frameCounter % 4 == 0)
		{
			ProcessChunkLoadQueue();
		}

		ProcessMeshApplyQueue();
		TickFurnaces((float)delta);
	}

	private void ProcessMeshApplyQueue()
	{
		if (_meshApplyQueue.TryDequeue(out var item))
		{
			if (GodotObject.IsInstanceValid(item.Chunk))
			{
				item.Chunk.ApplyMeshData(item.MeshData, item.TriggerNeighbors);
			}
		}
	}

	private void UpdateChunksAroundPlayer(Vector3 playerPos)
	{
		Vector2I playerChunk = GetChunkCoord(Mathf.FloorToInt(playerPos.X), Mathf.FloorToInt(playerPos.Z));
		
		List<Vector2I> newChunkPositions = new();

		// 1. Megkeressük a betöltendő chunk pozíciókat
		for (int x = playerChunk.X - RenderDistance; x <= playerChunk.X + RenderDistance; x++)
		for (int z = playerChunk.Y - RenderDistance; z <= playerChunk.Y + RenderDistance; z++)
		{
			var pos = new Vector2I(x, z);
			if (!_chunks.ContainsKey(pos))
			{
				newChunkPositions.Add(pos);
			}
		}

		// Újraépítjük a sort, hogy a legközelebbiek legyenek elöl
		_chunkLoadQueue.Clear();

		// Közelebbi chunkok prioritása (távolság alapján növekvő sorrend)
		newChunkPositions.Sort((a, b) => 
		{
			float distA = a.DistanceSquaredTo(playerChunk);
			float distB = b.DistanceSquaredTo(playerChunk);
			return distA.CompareTo(distB);
		});

		foreach (var pos in newChunkPositions)
		{
			_chunkLoadQueue.Enqueue(pos);
		}

		// 5. Túl távoli chunkok kicsatolása és törlése
		List<Vector2I> chunksToRemove = new();
		foreach (var pos in _chunks.Keys)
		{
			if (Math.Abs(pos.X - playerChunk.X) > RenderDistance + 1 ||
				Math.Abs(pos.Y - playerChunk.Y) > RenderDistance + 1)
			{
				chunksToRemove.Add(pos);
			}
		}
		foreach (var pos in chunksToRemove)
		{
			if (_chunks.TryGetValue(pos, out Chunk? chunk) && chunk != null)
			{
				chunk.QueueFree();
			}
			_chunks.Remove(pos);
		}
	}

	private void ProcessChunkLoadQueue()
	{
		// Indíthatunk egyszerre több generálási feladatot is, mivel háttérszálon futnak és nem akasztják a játékot
		int tasksStartedCount = 0;
		while (_chunkLoadQueue.Count > 0 && tasksStartedCount < 1)
		{
			Vector2I pos = _chunkLoadQueue.Dequeue();

			lock (_chunks)
			{
				if (_chunks.ContainsKey(pos) || _chunksLoading.ContainsKey(pos)) continue;
			}

			// Ellenőrizzük, hogy a játékos időközben nem mozdult-e el messzire
			if (_player != null)
			{
				Vector3 playerPos = _player.Position;
				Vector2I playerChunk = GetChunkCoord(Mathf.FloorToInt(playerPos.X), Mathf.FloorToInt(playerPos.Z));
				if (Math.Abs(pos.X - playerChunk.X) > RenderDistance + 1 ||
					Math.Abs(pos.Y - playerChunk.Y) > RenderDistance + 1)
				{
					continue;
				}
			}

			// Fő szálon instantiálunk (nagyon gyors)
			var chunkNode = ChunkScene.Instantiate<Chunk>();
			AddChild(chunkNode);
			chunkNode.Initialize(pos);
			
			lock (_chunks)
			{
				_chunks[pos] = chunkNode;
			}

			_chunksLoading[pos] = true;
			tasksStartedCount++;

			// Háttérszálon végezzük el a nehéz számításokat
			System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					GenerateChunkTerrain(chunkNode);
					GenerateChunkTrees(chunkNode);
					
					var meshData = chunkNode.BuildMeshData();
					
					// Behelyezzük a kész mesh-t a fő szál ütemező sorába
					_meshApplyQueue.Enqueue((chunkNode, meshData, true));
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Hiba a háttérben történő chunk generálás közben: {ex}");
				}
				finally
				{
					_chunksLoading.TryRemove(pos, out _);
				}
			});
		}
	}

	private void GenerateChunkTerrain(Chunk chunk)
	{
		int startX = chunk.ChunkPosition.X * Chunk.ChunkSizeX;
		int startZ = chunk.ChunkPosition.Y * Chunk.ChunkSizeZ;

		for (int x = 0; x < Chunk.ChunkSizeX; x++)
		for (int z = 0; z < Chunk.ChunkSizeZ; z++)
		{
			int globalX = startX + x;
			int globalZ = startZ + z;

			// Zaj lekérése [-1.0, 1.0] között
			float noiseVal = _noise.GetNoise2D(globalX, globalZ);

			// Domborzat magassága: pl. 10 és 30 blokk között
			int height = Mathf.RoundToInt((noiseVal + 1.0f) * 10.0f) + 10;

			// Biztonsági korlátok
			height = Mathf.Clamp(height, 1, Chunk.ChunkSizeY - 10);

			for (int y = 0; y < Chunk.ChunkSizeY; y++)
			{
				if (y > height)
				{
					chunk.SetBlock(x, y, z, BlockType.Air);
				}
				else if (y == height)
				{
					chunk.SetBlock(x, y, z, BlockType.Grass);
				}
				else if (y >= height - 3)
				{
					chunk.SetBlock(x, y, z, BlockType.Dirt);
				}
				else
				{
					// Ércek véletlenszerű generálása a kőzetben (mélység és esély alapján)
					BlockType blockToPlace = BlockType.Stone;
					double roll = Random.Shared.NextDouble();

					if (y < 15 && roll < 0.003) // Gyémánt mélyen (y < 15) és nagyon ritka
					{
						blockToPlace = BlockType.DiamondOre;
					}
					else if (y < height - 15 && roll < 0.008) // Arany mélyebben
					{
						blockToPlace = BlockType.GoldOre;
					}
					else if (y < height - 10 && roll < 0.015) // Vas
					{
						blockToPlace = BlockType.IronOre;
					}
					else if (y < height - 5 && roll < 0.025) // Szén
					{
						blockToPlace = BlockType.CoalOre;
					}

					chunk.SetBlock(x, y, z, blockToPlace);
				}
			}
		}
	}

	private void GenerateChunkTrees(Chunk chunk)
	{
		int startX = chunk.ChunkPosition.X * Chunk.ChunkSizeX;
		int startZ = chunk.ChunkPosition.Y * Chunk.ChunkSizeZ;

		for (int x = 0; x < Chunk.ChunkSizeX; x++)
		for (int z = 0; z < Chunk.ChunkSizeZ; z++)
		{
			int topY = GetTopBlockYLocal(chunk, x, z);
			if (topY < 0) continue;

			BlockType topBlock = chunk.GetBlock(x, topY, z);
			if (topBlock == BlockType.Grass)
			{
				// 1.5% eséllyel fát növesztünk a fű tetejére
				if (Random.Shared.NextDouble() < 0.015)
				{
					int globalX = startX + x;
					int globalZ = startZ + z;
					SpawnTree(new Vector3I(globalX, topY, globalZ));
				}
			}
		}
	}

	private int GetTopBlockYLocal(Chunk chunk, int x, int z)
	{
		for (int y = Chunk.ChunkSizeY - 1; y >= 0; y--)
		{
			if (chunk.GetBlock(x, y, z) != BlockType.Air)
			{
				return y;
			}
		}
		return -1;
	}

	private void SpawnTree(Vector3I basePos)
	{
		// A fatörzs magassága 4 és 6 blokk között változik
		int trunkHeight = Random.Shared.Next(4, 7);

		// 1. Fatörzs elhelyezése
		for (int i = 1; i <= trunkHeight; i++)
		{
			SetBlockAtGlobalNoUpdate(new Vector3I(basePos.X, basePos.Y + i, basePos.Z), BlockType.Wood);
		}

		// 2. Lombkorona elhelyezése (lomb 5x5x3, fent 3x3)
		int leavesYStart = basePos.Y + trunkHeight - 1;
		int leavesYEnd = basePos.Y + trunkHeight + 2;

		for (int y = leavesYStart; y <= leavesYEnd; y++)
		{
			int radius = 2;
			if (y == leavesYEnd) radius = 1;

			for (int xOffset = -radius; xOffset <= radius; xOffset++)
			for (int zOffset = -radius; zOffset <= radius; zOffset++)
			{
				// Sarkok elhagyása a kerekebb lombkoronáért
				if (Math.Abs(xOffset) == radius && Math.Abs(zOffset) == radius)
				{
					if (y >= leavesYEnd - 1 || Random.Shared.NextDouble() < 0.5)
						continue;
				}

				Vector3I leafPos = new Vector3I(basePos.X + xOffset, y, basePos.Z + zOffset);

				// Ne írjuk felül a fa törzsét lombbal
				if (leafPos.X == basePos.X && leafPos.Z == basePos.Z && y <= basePos.Y + trunkHeight)
					continue;

				SetBlockAtGlobalNoUpdate(leafPos, BlockType.Leaves);
			}
		}
	}

	public bool SetBlockAtGlobalNoUpdate(Vector3I globalPos, BlockType type)
	{
		lock (_chunks)
		{
			if (globalPos.Y < 0 || globalPos.Y >= Chunk.ChunkSizeY) return false;
			Vector2I chunkPos = GetChunkCoord(globalPos.X, globalPos.Z);

			if (_chunks.TryGetValue(chunkPos, out Chunk? chunk) && chunk != null)
			{
				int lx = globalPos.X - chunk.ChunkPosition.X * Chunk.ChunkSizeX;
				int lz = globalPos.Z - chunk.ChunkPosition.Y * Chunk.ChunkSizeZ;
				
				BlockType oldType = chunk.GetBlock(lx, globalPos.Y, lz);
				if (oldType != type)
				{
					chunk.SetBlock(lx, globalPos.Y, lz, type);
					chunk.IsDirty = true;
				}
				return true;
			}
			return false;
		}
	}

	public BlockType GetBlockAtGlobal(Vector3I globalPos)
	{
		lock (_chunks)
		{
			if (globalPos.Y < 0 || globalPos.Y >= Chunk.ChunkSizeY) return BlockType.Air;
			Vector2I chunkPos = GetChunkCoord(globalPos.X, globalPos.Z);
			
			if (_chunks.TryGetValue(chunkPos, out Chunk? chunk) && chunk != null)
			{
				int lx = globalPos.X - chunk.ChunkPosition.X * Chunk.ChunkSizeX;
				int lz = globalPos.Z - chunk.ChunkPosition.Y * Chunk.ChunkSizeZ;
				return chunk.GetBlock(lx, globalPos.Y, lz);
			}
			return BlockType.Air;
		}
	}

	public bool SetBlockAtGlobal(Vector3I globalPos, BlockType type)
	{
		lock (_chunks)
		{
			if (globalPos.Y < 0 || globalPos.Y >= Chunk.ChunkSizeY) return false;
			Vector2I chunkPos = GetChunkCoord(globalPos.X, globalPos.Z);
			
			if (_chunks.TryGetValue(chunkPos, out Chunk? chunk) && chunk != null)
			{
				int lx = globalPos.X - chunk.ChunkPosition.X * Chunk.ChunkSizeX;
				int lz = globalPos.Z - chunk.ChunkPosition.Y * Chunk.ChunkSizeZ;
				
				chunk.SetBlock(lx, globalPos.Y, lz, type);
				chunk.GenerateMesh();

				// Szomszédos chunkok hálóinak frissítése szinkron módon a játékos interakciókra
				if (lx == 0) UpdateNeighborMeshSync(chunkPos + new Vector2I(-1, 0));
				if (lx == Chunk.ChunkSizeX - 1) UpdateNeighborMeshSync(chunkPos + new Vector2I(1, 0));
				if (lz == 0) UpdateNeighborMeshSync(chunkPos + new Vector2I(0, -1));
				if (lz == Chunk.ChunkSizeZ - 1) UpdateNeighborMeshSync(chunkPos + new Vector2I(0, 1));

				return true;
			}
			return false;
		}
	}

	public void UpdateNeighborMesh(Vector2I neighborPos)
	{
		lock (_chunks)
		{
			if (_chunks.TryGetValue(neighborPos, out Chunk? neighbor) && neighbor != null)
			{
				if (_chunksLoading.ContainsKey(neighborPos)) return;

				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						var meshData = neighbor.BuildMeshData();
						_meshApplyQueue.Enqueue((neighbor, meshData, false));
					}
					catch (Exception ex)
					{
						GD.PrintErr($"Hiba a szomszédos mesh építés közben: {ex}");
					}
				});
			}
		}
	}

	private void UpdateNeighborMeshSync(Vector2I neighborPos)
	{
		lock (_chunks)
		{
			if (_chunks.TryGetValue(neighborPos, out Chunk? neighbor) && neighbor != null)
			{
				neighbor.GenerateMesh();
			}
		}
	}

	public Vector2I GetChunkCoord(int globalX, int globalZ)
	{
		int cx = globalX >= 0 ? globalX / Chunk.ChunkSizeX : (globalX - Chunk.ChunkSizeX + 1) / Chunk.ChunkSizeX;
		int cz = globalZ >= 0 ? globalZ / Chunk.ChunkSizeZ : (globalZ - Chunk.ChunkSizeZ + 1) / Chunk.ChunkSizeZ;
		return new Vector2I(cx, cz);
	}

	public void SpawnBreakingParticles(Vector3 position, BlockType type)
	{
		var particles = new CpuParticles3D();
		particles.Position = position;
		particles.Amount = 10;
		particles.OneShot = true;
		particles.Explosiveness = 1.0f;
		particles.Lifetime = 0.4f;
		particles.Direction = Vector3.Up;
		particles.Spread = 45f;
		particles.InitialVelocityMin = 1.5f;
		particles.InitialVelocityMax = 3.0f;
		particles.Gravity = new Vector3(0, -9.8f, 0);

		var material = new StandardMaterial3D
		{
			AlbedoColor = GetBlockColor(type),
			Roughness = 1.0f,
			Metallic = 0.0f
		};
		particles.MaterialOverride = material;

		var mesh = new BoxMesh();
		mesh.Size = new Vector3(0.06f, 0.06f, 0.06f);
		particles.Mesh = mesh;

		AddChild(particles);

		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += () => particles.QueueFree();
	}

	private Color GetBlockColor(BlockType type)
	{
		return type switch
		{
			BlockType.Grass => new Color(0.3f, 0.6f, 0.2f),
			BlockType.Dirt => new Color(0.5f, 0.35f, 0.2f),
			BlockType.Stone => new Color(0.5f, 0.5f, 0.5f),
			BlockType.Wood => new Color(0.4f, 0.3f, 0.15f),
			BlockType.Leaves => new Color(0.15f, 0.4f, 0.15f),
			BlockType.Brick => new Color(0.7f, 0.3f, 0.3f),
			BlockType.Planks => new Color(0.65f, 0.5f, 0.3f),
			BlockType.Glass => new Color(0.8f, 0.9f, 1.0f, 0.5f),
			BlockType.Cobblestone => new Color(0.4f, 0.4f, 0.4f),
			BlockType.CoalOre => new Color(0.3f, 0.3f, 0.3f),
			BlockType.IronOre => new Color(0.7f, 0.6f, 0.5f),
			BlockType.GoldOre => new Color(0.9f, 0.8f, 0.2f),
			BlockType.DiamondOre => new Color(0.3f, 0.8f, 0.9f),
			_ => new Color(1, 1, 1)
		};
	}

	public void SpawnBlockItem(Vector3 position, BlockType type)
	{
		var item = new BlockItem();
		item.Type = type;
		item.Position = position;
		AddChild(item);
	}

	private void TickFurnaces(float delta)
	{
		if (Furnaces.Count == 0) return;
		
		List<Vector3I> toRemove = new();
		foreach (var kvp in Furnaces)
		{
			var pos = kvp.Key;
			var state = kvp.Value;

			if (GetBlockAtGlobal(pos) != BlockType.Furnace)
			{
				toRemove.Add(pos);
				continue;
			}

			bool isBurning = state.BurnTimeLeft > 0f;
			bool canSmelt = CanSmelt(state);

			if (isBurning)
			{
				state.BurnTimeLeft -= delta;
				if (state.BurnTimeLeft < 0f) state.BurnTimeLeft = 0f;
			}

			if (state.BurnTimeLeft <= 0f && canSmelt)
			{
				if (TryConsumeFuel(state))
				{
					isBurning = true;
				}
			}

			if (state.BurnTimeLeft > 0f && canSmelt)
			{
				state.SmeltProgress += delta;
				if (state.SmeltProgress >= 10f) // 10 seconds to smelt
				{
					state.SmeltProgress = 0f;
					SmeltItem(state);
				}
			}
			else
			{
				state.SmeltProgress = 0f;
			}
		}

		foreach (var pos in toRemove)
		{
			Furnaces.Remove(pos);
		}
	}

	private bool CanSmelt(FurnaceState state)
	{
		if (state.Input.IsEmpty) return false;
		BlockType result = GetSmeltResult(state.Input.Type);
		if (result == BlockType.Air) return false;

		if (state.Output.IsEmpty) return true;
		if (state.Output.Type != result) return false;
		if (state.Output.Count >= 64) return false;

		return true;
	}

	public static BlockType GetSmeltResult(BlockType input)
	{
		return input switch
		{
			BlockType.RawPorkchop => BlockType.CookedPorkchop,
			BlockType.Cobblestone => BlockType.Stone,
			BlockType.CoalOre => BlockType.DiamondOre,
			_ => BlockType.Air
		};
	}

	public static int GetFuelBurnTime(BlockType fuel)
	{
		return fuel switch
		{
			BlockType.CoalOre => 80, // Smelts 8 items (10 seconds each)
			BlockType.Wood => 15,
			BlockType.Planks => 15,
			BlockType.Stick => 5,
			BlockType.Leaves => 2,
			_ => 0
		};
	}

	private bool TryConsumeFuel(FurnaceState state)
	{
		if (state.Fuel.IsEmpty) return false;
		int burnTime = GetFuelBurnTime(state.Fuel.Type);
		if (burnTime > 0)
		{
			state.MaxBurnTime = burnTime;
			state.BurnTimeLeft = burnTime;
			
			var fuelStack = state.Fuel;
			fuelStack.Count--;
			if (fuelStack.Count <= 0) state.Fuel = ItemStack.Empty;
			else state.Fuel = fuelStack;
			return true;
		}
		return false;
	}

	private void SmeltItem(FurnaceState state)
	{
		BlockType result = GetSmeltResult(state.Input.Type);
		
		var inputStack = state.Input;
		inputStack.Count--;
		if (inputStack.Count <= 0) state.Input = ItemStack.Empty;
		else state.Input = inputStack;

		if (state.Output.IsEmpty)
		{
			state.Output = new ItemStack(result, 1);
		}
		else
		{
			var outputStack = state.Output;
			outputStack.Count++;
			state.Output = outputStack;
		}
	}
}

public class FurnaceState
{
	public ItemStack Input = ItemStack.Empty;
	public ItemStack Fuel = ItemStack.Empty;
	public ItemStack Output = ItemStack.Empty;
	public float BurnTimeLeft = 0f;
	public float MaxBurnTime = 0f;
	public float SmeltProgress = 0f;
}
