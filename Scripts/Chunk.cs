using Godot;
using System;
using System.Collections.Generic;

namespace Minecraft;

public partial class Chunk : StaticBody3D
{
	public const int ChunkSizeX = 16;
	public const int ChunkSizeY = 64;
	public const int ChunkSizeZ = 16;

	public BlockType[,,] Blocks { get; private set; } = new BlockType[ChunkSizeX, ChunkSizeY, ChunkSizeZ];
	public Vector2I ChunkPosition { get; set; }
	public bool IsDirty { get; set; } = false;

	private MeshInstance3D _meshInstance = null!;
	private CollisionShape3D _collisionShape = null!;
	private World _world = null!;

	private static StandardMaterial3D? _sharedMaterial;
	private static StandardMaterial3D? _sharedItemMaterial;

	private static StandardMaterial3D GetSharedMaterial()
	{
		if (_sharedMaterial == null)
		{
			_sharedMaterial = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>("res://Tilemap/terrain.png"),
				TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
				CullMode = BaseMaterial3D.CullModeEnum.Back,
				Roughness = 1.0f,
				Metallic = 0.0f,
				MetallicSpecular = 0.0f
			};
		}
		return _sharedMaterial;
	}

	private static StandardMaterial3D GetSharedItemMaterial()
	{
		if (_sharedItemMaterial == null)
		{
			_sharedItemMaterial = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>("res://Tilemap/items_256x32.png"),
				TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled, // Double sided
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 1.0f,
				Metallic = 0.0f
			};
		}
		return _sharedItemMaterial;
	}

	private static readonly Vector3I[] Directions =
	{
		new(-1,0,0), new(1,0,0), new(0,0,-1),
		new(0,0,1),  new(0,1,0), new(0,-1,0)
	};

	// Precíz CCW csúcspontok, amelyek tökéletesen lefedik a Godot 3D-s koordináta-rendszerét
	private static readonly Vector3[][] FaceVertices =
	{
		// Bal (-X)
		new[]{ new Vector3(0,0,1), new(0,0,0), new(0,1,0), new(0,1,1) },
		// Jobb (+X)
		new[]{ new Vector3(1,0,0), new(1,0,1), new(1,1,1), new(1,1,0) },
		// Hátul (-Z)
		new[]{ new Vector3(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0) },
		// Elöl (+Z)
		new[]{ new Vector3(1,0,1), new(0,0,1), new(0,1,1), new(1,1,1) },
		// Fent (+Y)
		new[]{ new Vector3(0,1,1), new(1,1,1), new(1,1,0), new(0,1,0) },
		// Lent (-Y)
		new[]{ new Vector3(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1) }
	};

	public override void _Ready()
	{
		_meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
		_collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
		_world = GetParent<World>();
	}

	public void Initialize(Vector2I pos)
	{
		ChunkPosition = pos;
		Position = new Vector3(pos.X * ChunkSizeX, 0, pos.Y * ChunkSizeZ);
	}

	public BlockType GetBlock(int x, int y, int z)
	{
		lock (Blocks)
		{
			if (x >= 0 && x < ChunkSizeX && y >= 0 && y < ChunkSizeY && z >= 0 && z < ChunkSizeZ)
				return Blocks[x, y, z];
			return BlockType.Air;
		}
	}

	public void SetBlock(int x, int y, int z, BlockType type)
	{
		lock (Blocks)
		{
			if (x >= 0 && x < ChunkSizeX && y >= 0 && y < ChunkSizeY && z >= 0 && z < ChunkSizeZ)
				Blocks[x, y, z] = type;
		}
	}

	public partial class MeshData : GodotObject
	{
		public ArrayMesh? Mesh;
		public Vector3[]? CollisionFaces;
	}

	public MeshData BuildMeshData()
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		float uSize = 1f / BlockDatabase.AtlasCols;
		float vSize = 1f / BlockDatabase.AtlasRows;
		const float Margin = 0.0002f;

		bool hasGeometry = false;

		BlockType[,,] localBlocks;
		lock (Blocks)
		{
			localBlocks = (BlockType[,,])Blocks.Clone();
		}

		for (int x = 0; x < ChunkSizeX; x++)
		for (int y = 0; y < ChunkSizeY; y++)
		for (int z = 0; z < ChunkSizeZ; z++)
		{
			BlockType block = localBlocks[x, y, z];
			if (block == BlockType.Air) continue;

			for (int f = 0; f < 6; f++)
			{
				Vector3I dir = Directions[f];
				int nx = x + dir.X, ny = y + dir.Y, nz = z + dir.Z;

				bool showFace = false;
				if (nx < 0 || nx >= ChunkSizeX || ny < 0 || ny >= ChunkSizeY || nz < 0 || nz >= ChunkSizeZ)
				{
					Vector3I gp = new Vector3I(ChunkPosition.X * ChunkSizeX + nx, ny, ChunkPosition.Y * ChunkSizeZ + nz);
					showFace = BlockDatabase.IsTransparent(_world.GetBlockAtGlobal(gp));
				}
				else
				{
					showFace = BlockDatabase.IsTransparent(localBlocks[nx, ny, nz]);
				}

				if (!showFace) continue;
				hasGeometry = true;

				int texIndex = BlockDatabase.GetTextureIndex(block, f);
				int col = texIndex % BlockDatabase.AtlasCols;
				int row = texIndex / BlockDatabase.AtlasCols;

				float u1 = col * uSize + Margin;
				float u2 = (col + 1) * uSize - Margin;
				float vVal1 = row * vSize + Margin;
				float vVal2 = (row + 1) * vSize - Margin;

				Vector3 bp = new Vector3(x, y, z);

				Vector2 uv0 = new Vector2(u1, vVal2);
				Vector2 uv1 = new Vector2(u2, vVal2);
				Vector2 uv2 = new Vector2(u2, vVal1);
				Vector2 uv3 = new Vector2(u1, vVal1);

				if (f == 0 || f == 1)
				{
					uv0 = new Vector2(u2, vVal2);
					uv1 = new Vector2(u1, vVal2);
					uv2 = new Vector2(u1, vVal1);
					uv3 = new Vector2(u2, vVal1);
				}

				if (f < 4) // Side faces (Left, Right, Back, Front) - standard winding
				{
					st.SetNormal((Vector3)dir); st.SetUV(uv0); st.AddVertex(bp + FaceVertices[f][0]);
					st.SetNormal((Vector3)dir); st.SetUV(uv1); st.AddVertex(bp + FaceVertices[f][1]);
					st.SetNormal((Vector3)dir); st.SetUV(uv2); st.AddVertex(bp + FaceVertices[f][2]);

					st.SetNormal((Vector3)dir); st.SetUV(uv0); st.AddVertex(bp + FaceVertices[f][0]);
					st.SetNormal((Vector3)dir); st.SetUV(uv2); st.AddVertex(bp + FaceVertices[f][2]);
					st.SetNormal((Vector3)dir); st.SetUV(uv3); st.AddVertex(bp + FaceVertices[f][3]);
				}
				else // Top and Bottom faces - keep inverted winding
				{
					st.SetNormal((Vector3)dir); st.SetUV(uv0); st.AddVertex(bp + FaceVertices[f][0]);
					st.SetNormal((Vector3)dir); st.SetUV(uv2); st.AddVertex(bp + FaceVertices[f][2]);
					st.SetNormal((Vector3)dir); st.SetUV(uv1); st.AddVertex(bp + FaceVertices[f][1]);

					st.SetNormal((Vector3)dir); st.SetUV(uv0); st.AddVertex(bp + FaceVertices[f][0]);
					st.SetNormal((Vector3)dir); st.SetUV(uv3); st.AddVertex(bp + FaceVertices[f][3]);
					st.SetNormal((Vector3)dir); st.SetUV(uv2); st.AddVertex(bp + FaceVertices[f][2]);
				}
			}
		}

		if (!hasGeometry)
		{
			return new MeshData();
		}

		st.Index();
		var mesh = st.Commit();
		if (mesh == null) return new MeshData();

		return new MeshData
		{
			Mesh = mesh,
			CollisionFaces = mesh.GetFaces()
		};
	}

	public void ApplyMeshData(MeshData data)
	{
		ApplyMeshData(data, true);
	}

	public void ApplyMeshData(MeshData data, bool triggerNeighbors)
	{
		if (data.Mesh == null)
		{
			_meshInstance.Mesh = null;
			_collisionShape.Shape = null;
			
			// Szomszédok frissítése, ha a háló eltűnt (pl. üres chunk lett)
			if (triggerNeighbors)
			{
				TriggerNeighborMeshUpdates();
			}
			return;
		}

		data.Mesh.SurfaceSetMaterial(0, GetSharedMaterial());
		_meshInstance.Mesh = data.Mesh;

		var collision = new ConcavePolygonShape3D();
		collision.Data = data.CollisionFaces;
		collision.BackfaceCollision = true;
		_collisionShape.Position = Vector3.Zero;
		_collisionShape.Shape = collision;

		// Szomszédok frissítése, hátha a lombok átnyúltak
		if (triggerNeighbors)
		{
			TriggerNeighborMeshUpdates();
		}
	}

	private void TriggerNeighborMeshUpdates()
	{
		if (_world != null)
		{
			_world.CallDeferred("UpdateNeighborMesh", ChunkPosition + new Vector2I(-1, 0));
			_world.CallDeferred("UpdateNeighborMesh", ChunkPosition + new Vector2I(1, 0));
			_world.CallDeferred("UpdateNeighborMesh", ChunkPosition + new Vector2I(0, -1));
			_world.CallDeferred("UpdateNeighborMesh", ChunkPosition + new Vector2I(0, 1));
		}
	}

	public void GenerateMesh()
	{
		var data = BuildMeshData();
		ApplyMeshData(data);
	}

	public static Mesh CreateBlockMesh(BlockType type)
	{
		int itemIndex = Player.GetItemTextureIndex(type);
		if (itemIndex >= 0)
		{
			// Render flat item sprite
			var st = new SurfaceTool();
			st.Begin(Mesh.PrimitiveType.Triangles);
			
			float s = 0.15f; // size of the sprite in 3D
			
			// Vertex positions for a double-sided quad centered at (0,0,0)
			Vector3 v0 = new Vector3(-s, -s, 0);
			Vector3 v1 = new Vector3(s, -s, 0);
			Vector3 v2 = new Vector3(s, s, 0);
			Vector3 v3 = new Vector3(-s, s, 0);
			
			// UV coordinates for the item in items_256x32.png
			float uSize = 1f / 16f; // 16 columns
			float vSize = 1f / 2f;  // 2 rows
			int col = itemIndex % 16;
			int row = itemIndex / 16;
			
			float u1 = col * uSize;
			float u2 = (col + 1) * uSize;
			float vVal1 = row * vSize;
			float vVal2 = (row + 1) * vSize;
			
			Vector2 uv0 = new Vector2(u1, vVal2);
			Vector2 uv1 = new Vector2(u2, vVal2);
			Vector2 uv2 = new Vector2(u2, vVal1);
			Vector2 uv3 = new Vector2(u1, vVal1);
			
			// Front Face
			st.SetNormal(Vector3.Back); st.SetUV(uv0); st.AddVertex(v0);
			st.SetNormal(Vector3.Back); st.SetUV(uv1); st.AddVertex(v1);
			st.SetNormal(Vector3.Back); st.SetUV(uv2); st.AddVertex(v2);
			
			st.SetNormal(Vector3.Back); st.SetUV(uv0); st.AddVertex(v0);
			st.SetNormal(Vector3.Back); st.SetUV(uv2); st.AddVertex(v2);
			st.SetNormal(Vector3.Back); st.SetUV(uv3); st.AddVertex(v3);
			
			// Back Face (double-sided)
			st.SetNormal(Vector3.Forward); st.SetUV(uv1); st.AddVertex(v1);
			st.SetNormal(Vector3.Forward); st.SetUV(uv0); st.AddVertex(v0);
			st.SetNormal(Vector3.Forward); st.SetUV(uv3); st.AddVertex(v3);
			
			st.SetNormal(Vector3.Forward); st.SetUV(uv1); st.AddVertex(v1);
			st.SetNormal(Vector3.Forward); st.SetUV(uv3); st.AddVertex(v3);
			st.SetNormal(Vector3.Forward); st.SetUV(uv2); st.AddVertex(v2);
			
			st.Index();
			var mesh = st.Commit();
			if (mesh == null) return new BoxMesh { Size = new Vector3(0.25f, 0.25f, 0.25f) };
			
			mesh.SurfaceSetMaterial(0, GetSharedItemMaterial());
			return mesh;
		}

		var stCube = new SurfaceTool();
		stCube.Begin(Mesh.PrimitiveType.Triangles);

		float uSizeBlock = 1f / BlockDatabase.AtlasCols;
		float vSizeBlock = 1f / BlockDatabase.AtlasRows;
		const float Margin = 0.0002f;

		// 0.25 egység méretű mini blokk
		float sb = 0.125f;
		Vector3[][] vertices = new Vector3[][]
		{
			// Bal (-X)
			new[]{ new Vector3(-sb,-sb,sb), new(-sb,-sb,-sb), new(-sb,sb,-sb), new(-sb,sb,sb) },
			// Jobb (+X)
			new[]{ new Vector3(sb,-sb,-sb), new(sb,-sb,sb), new(sb,sb,sb), new(sb,sb,-sb) },
			// Hátul (-Z)
			new[]{ new Vector3(-sb,-sb,-sb), new(sb,-sb,-sb), new(sb,sb,-sb), new(-sb,sb,-sb) },
			// Elöl (+Z)
			new[]{ new Vector3(sb,-sb,sb), new(-sb,-sb,sb), new(-sb,sb,sb), new(sb,sb,sb) },
			// Fent (+Y)
			new[]{ new Vector3(-sb,sb,sb), new(sb,sb,sb), new(sb,sb,-sb), new(-sb,sb,-sb) },
			// Lent (-Y)
			new[]{ new Vector3(-sb,-sb,-sb), new(sb,-sb,-sb), new(sb,-sb,sb), new(-sb,-sb,sb) }
		};

		for (int f = 0; f < 6; f++)
		{
			Vector3 dir = (Vector3)Directions[f];
			int texIndex = BlockDatabase.GetTextureIndex(type, f);
			int col = texIndex % BlockDatabase.AtlasCols;
			int row = texIndex / BlockDatabase.AtlasCols;

			float u1 = col * uSizeBlock + Margin;
			float u2 = (col + 1) * uSizeBlock - Margin;
			float v1 = row * vSizeBlock + Margin;
			float v2 = (row + 1) * vSizeBlock - Margin;

			Vector2 uv0 = new Vector2(u1, v2);
			Vector2 uv1 = new Vector2(u2, v2);
			Vector2 uv2 = new Vector2(u2, v1);
			Vector2 uv3 = new Vector2(u1, v1);

			if (f == 0 || f == 1)
			{
				uv0 = new Vector2(u2, v2);
				uv1 = new Vector2(u1, v2);
				uv2 = new Vector2(u1, v1);
				uv3 = new Vector2(u2, v1);
			}

			if (f < 4)
			{
				stCube.SetNormal(dir); stCube.SetUV(uv0); stCube.AddVertex(vertices[f][0]);
				stCube.SetNormal(dir); stCube.SetUV(uv1); stCube.AddVertex(vertices[f][1]);
				stCube.SetNormal(dir); stCube.SetUV(uv2); stCube.AddVertex(vertices[f][2]);

				stCube.SetNormal(dir); stCube.SetUV(uv0); stCube.AddVertex(vertices[f][0]);
				stCube.SetNormal(dir); stCube.SetUV(uv2); stCube.AddVertex(vertices[f][2]);
				stCube.SetNormal(dir); stCube.SetUV(uv3); stCube.AddVertex(vertices[f][3]);
			}
			else
			{
				stCube.SetNormal(dir); stCube.SetUV(uv0); stCube.AddVertex(vertices[f][0]);
				stCube.SetNormal(dir); stCube.SetUV(uv2); stCube.AddVertex(vertices[f][2]);
				stCube.SetNormal(dir); stCube.SetUV(uv1); stCube.AddVertex(vertices[f][1]);

				stCube.SetNormal(dir); stCube.SetUV(uv0); stCube.AddVertex(vertices[f][0]);
				stCube.SetNormal(dir); stCube.SetUV(uv3); stCube.AddVertex(vertices[f][3]);
				stCube.SetNormal(dir); stCube.SetUV(uv2); stCube.AddVertex(vertices[f][2]);
			}
		}

		stCube.Index();
		var meshBlock = stCube.Commit();
		if (meshBlock == null) return new BoxMesh { Size = new Vector3(0.25f, 0.25f, 0.25f) };

		meshBlock.SurfaceSetMaterial(0, GetSharedMaterial());
		return meshBlock;
	}
}
