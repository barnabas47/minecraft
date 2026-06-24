using Godot;
using System;

namespace Minecraft;

public partial class Player : CharacterBody3D
{
	[Export] public float Speed { get; set; } = 5.0f;
	[Export] public float JumpVelocity { get; set; } = 5.745f; //(gyok(2 * g * H))
	[Export] public float MouseSensitivity { get; set; } = 0.003f;

	private Camera3D _camera = null!;
	private RayCast3D _rayCast = null!;
	private World _world = null!;
	private Label _hotbarLabel = null!;

	private float _gravity = 15.0f;
	private int _selectedHotbarIndex = 0;
	private BlockType SelectedBlock => _hotbarBlocks[_selectedHotbarIndex].IsEmpty ? BlockType.Air : _hotbarBlocks[_selectedHotbarIndex].Type;
	private bool _physicsWokenUp = false;

	// Status bars state
	private float _health = 20f;
	private const float MaxHealth = 20f;
	private float _hunger = 20f;
	private const float MaxHunger = 20f;
	private float _xpProgress = 0f;
	private int _xpLevel = 0;

	// Status bars UI
	private ColorRect _healthBarFill = null!;
	private ColorRect _hungerBarFill = null!;
	private ColorRect _xpBarFill = null!;

	// Timer state for hunger and regen
	private float _hungerTimer = 0f;
	private float _starvationTimer = 0f;
	private float _regenTimer = 0f;

	// Fall damage state
	private float _highestYInAir = float.MinValue;
	private bool _wasInAir = false;

	// Bányászati segédváltozók
	private Vector3I? _miningBlockPos = null;
	private float _miningProgress = 0f;
	private float _particleTimer = 0f;
	private ProgressBar _miningProgressBar = null!;
	private MeshInstance3D _miningOverlay = null!;
	private Texture2D _breakingTexture = null!;

	// Hotbar segédváltozók
	private TextureRect[] _hotbarSlots = null!;
	private readonly ItemStack[] _hotbarBlocks = new ItemStack[]
	{
		new ItemStack(BlockType.Grass, 64),
		new ItemStack(BlockType.Dirt, 64),
		new ItemStack(BlockType.Stone, 64),
		new ItemStack(BlockType.Wood, 64),
		new ItemStack(BlockType.Leaves, 64),
		new ItemStack(BlockType.Brick, 64),
		new ItemStack(BlockType.Planks, 64),
		new ItemStack(BlockType.Glass, 64),
		new ItemStack(BlockType.Cobblestone, 64)
	};

	// Inventory segédváltozók
	private bool _isInventoryOpen = false;
	private bool _isCraftingTableOpen = false;
	private bool _isFurnaceOpen = false;
	private Vector3I _openFurnacePos = Vector3I.Zero;
	private Control _inventoryMenu = null!;
	private TextureRect _inventoryPanel = null!;
	private ItemStack _cursorItem = ItemStack.Empty;
	private TextureRect _cursorIcon = null!;
	private readonly ItemStack[] _inventoryBlocks = new ItemStack[36];
	private readonly ItemStack[] _armorBlocks = new ItemStack[4];
	private readonly ItemStack[] _craftingBlocks = new ItemStack[4];
	private ItemStack _craftingResultBlock = ItemStack.Empty;
	private readonly ItemStack[] _tableCraftingBlocks = new ItemStack[9];
	private ItemStack _tableCraftingResultBlock = ItemStack.Empty;
	private readonly System.Collections.Generic.List<InventorySlot> _inventorySlotsList = new();

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;

		_camera = GetNode<Camera3D>("Camera3D");
		_rayCast = GetNode<RayCast3D>("Camera3D/RayCast3D");
		
		// Dinamikus ugrási sebesség beállítása a gravitáció és a kívánt 1.1 blokkos ugrásmagasság alapján
		JumpVelocity = Mathf.Sqrt(2.0f * _gravity * 1.1f);

		var worldNode = GetParent();
		while (worldNode != null && worldNode is not World)
		{
			worldNode = worldNode.GetParent();
		}
		_world = (World)worldNode!;

		CreateHotbarUI();
		CreateStatusBarsUI();
		CreateInventoryUI();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.E || keyEvent.Keycode == Key.I)
			{
				ToggleInventory();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (_isInventoryOpen)
		{
			return; // Ne dolgozzuk fel a mozgást/nézést ha nyitva az inventory
		}

		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			RotateY(-mouseMotion.Relative.X * MouseSensitivity);
			_camera.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);

			Vector3 camRot = _camera.Rotation;
			camRot.X = Mathf.Clamp(camRot.X, Mathf.DegToRad(-89), Mathf.DegToRad(89));
			_camera.Rotation = camRot;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
				? Input.MouseModeEnum.Visible 
				: Input.MouseModeEnum.Captured;
		}

		if (@event is InputEventKey slotKeyEvent && slotKeyEvent.Pressed)
		{
			int slotIndex = -1;
			if (slotKeyEvent.Keycode == Key.Key1) slotIndex = 0;
			else if (slotKeyEvent.Keycode == Key.Key2) slotIndex = 1;
			else if (slotKeyEvent.Keycode == Key.Key3) slotIndex = 2;
			else if (slotKeyEvent.Keycode == Key.Key4) slotIndex = 3;
			else if (slotKeyEvent.Keycode == Key.Key5) slotIndex = 4;
			else if (slotKeyEvent.Keycode == Key.Key6) slotIndex = 5;
			else if (slotKeyEvent.Keycode == Key.Key7) slotIndex = 6;
			else if (slotKeyEvent.Keycode == Key.Key8) slotIndex = 7;
			else if (slotKeyEvent.Keycode == Key.Key9) slotIndex = 8;

			if (slotIndex >= 0 && slotIndex < _hotbarBlocks.Length)
			{
				_selectedHotbarIndex = slotIndex;
				UpdateHotbarSelection();
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// FIZIKAI ÉBRESZTŐ: Ha indításkor beragadtál volna a levegőbe, 
		// ez a sor meglöki a motort, hogy azonnal kezdjen el számolni!
		if (!_physicsWokenUp)
		{
			this.MotionMode = MotionModeEnum.Grounded;
			_physicsWokenUp = true;
		}

		Vector3 velocity = Velocity;

		// Mennyezetbe ütközés: Ha beveri a fejét, azonnal szüntessük meg a felfelé irányuló sebességet, hogy egyből zuhanjon
		if (IsOnCeiling() && velocity.Y > 0)
		{
			velocity.Y = 0.0f;
		}

		if (!IsOnFloor())
		{
			velocity.Y -= _gravity * (float)delta;
		}
		else
		{
			velocity.Y = 0.0f;
		}

		if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		Vector2 inputDir = Vector2.Zero;
		if (!_isInventoryOpen)
		{
			inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
		}
		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * Speed;
			velocity.Z = direction.Z * Speed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		}

		Velocity = velocity;
		MoveAndSlide();

		HandleFallDamage(Position.Y);
		HandleHungerAndStarvation((float)delta);
		HandleHealthRegen((float)delta);

		HandleBlockInteraction(delta);
	}

	private void HandleBlockInteraction(double delta)
	{
		if (_isInventoryOpen || Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			ResetMining();
			return;
		}

		if (Input.IsActionJustPressed("click_right"))
		{
			BlockType heldType = SelectedBlock;
			if (IsFood(heldType))
			{
				EatFood(heldType);
				return;
			}
		}

		if (_rayCast.IsColliding())
		{
			// Helyezés (Jobb klikk)
			if (Input.IsActionJustPressed("click_right"))
			{
				// Megnézzük, hogy interakciós blokkra (pl. CraftingTable) kattintottunk-e
				Vector3 targetPoint = _rayCast.GetCollisionPoint() - _rayCast.GetCollisionNormal() * 0.1f;
				Vector3I targetPos = new Vector3I(
					Mathf.FloorToInt(targetPoint.X),
					Mathf.FloorToInt(targetPoint.Y),
					Mathf.FloorToInt(targetPoint.Z)
				);
				BlockType clickedWorldBlock = _world.GetBlockAtGlobal(targetPos);

				if (clickedWorldBlock == BlockType.CraftingTable)
				{
					OpenCraftingTableMenu();
				}
				else if (clickedWorldBlock == BlockType.Furnace)
				{
					OpenFurnaceMenu(targetPos);
				}
				else
				{
					Vector3 placementPoint = _rayCast.GetCollisionPoint() + _rayCast.GetCollisionNormal() * 0.1f;
					Vector3I blockPos = new Vector3I(
						Mathf.FloorToInt(placementPoint.X),
						Mathf.FloorToInt(placementPoint.Y),
						Mathf.FloorToInt(placementPoint.Z)
					);

					BlockType typeToPlace = SelectedBlock;
					if (typeToPlace != BlockType.Air && IsPlaceable(typeToPlace))
					{
						if (!IsCollidingWithPlayer(blockPos))
						{
							if (_world.SetBlockAtGlobal(blockPos, typeToPlace))
							{
								var stack = _hotbarBlocks[_selectedHotbarIndex];
								stack.Count -= 1;
								if (stack.Count <= 0)
								{
									_hotbarBlocks[_selectedHotbarIndex] = ItemStack.Empty;
								}
								else
								{
									_hotbarBlocks[_selectedHotbarIndex] = stack;
								}
								UpdateHotbarIcons();
							}
						}
					}
				}
			}

			// Bányászat (Bal klikk folyamatos nyomva tartása)
			if (Input.IsActionPressed("click_left"))
			{
				Vector3 hitPoint = _rayCast.GetCollisionPoint() - _rayCast.GetCollisionNormal() * 0.1f;
				Vector3I blockPos = new Vector3I(
					Mathf.FloorToInt(hitPoint.X),
					Mathf.FloorToInt(hitPoint.Y),
					Mathf.FloorToInt(hitPoint.Z)
				);

				BlockType hitBlock = _world.GetBlockAtGlobal(blockPos);

				if (hitBlock != BlockType.Air)
				{
					if (_miningBlockPos != blockPos)
					{
						_miningBlockPos = blockPos;
						_miningProgress = 0f;
						_particleTimer = 0f;
					}

					float mineTime = GetMiningTime(hitBlock, SelectedBlock);
					_miningProgress += (float)delta / mineTime;

					// Repedésvetítő Decal frissítése
					UpdateMiningDecal(blockPos, _miningProgress);

					// Por/törmelék részecskék szórása bányászat közben (0.15 másodpercenként)
					_particleTimer += (float)delta;
					if (_particleTimer >= 0.15f)
					{
						_particleTimer = 0f;
						_world.SpawnBreakingParticles(_rayCast.GetCollisionPoint(), hitBlock);
					}

					// Kamera finom rázkódása (a progress előrehaladtával nő a rázkódás)
					float shake = 0.006f * (_miningProgress + 0.2f);
					_camera.HOffset = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * shake;
					_camera.VOffset = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * shake;

					// Progress Bar frissítése
					if (_miningProgressBar != null)
					{
						_miningProgressBar.Visible = true;
						_miningProgressBar.Value = _miningProgress * 100f;
					}

					// Ha teljesen szétesett a blokk
					if (_miningProgress >= 1f)
					{
						_world.SetBlockAtGlobal(blockPos, BlockType.Air);
						
						Vector3 itemSpawnPos = new Vector3(blockPos.X + 0.5f, blockPos.Y + 0.5f, blockPos.Z + 0.5f);
						
						if (hitBlock == BlockType.Furnace)
						{
							// Spawn furnace block itself
							if (BlockDatabase.CanDropItem(hitBlock, SelectedBlock))
							{
								_world.SpawnBlockItem(itemSpawnPos, BlockType.Furnace);
							}
							
							// Drop items inside furnace
							if (_world.Furnaces.TryGetValue(blockPos, out var fState))
							{
								if (!fState.Input.IsEmpty)
								{
									for (int i = 0; i < fState.Input.Count; i++)
										_world.SpawnBlockItem(itemSpawnPos, fState.Input.Type);
								}
								if (!fState.Fuel.IsEmpty)
								{
									for (int i = 0; i < fState.Fuel.Count; i++)
										_world.SpawnBlockItem(itemSpawnPos, fState.Fuel.Type);
								}
								if (!fState.Output.IsEmpty)
								{
									for (int i = 0; i < fState.Output.Count; i++)
										_world.SpawnBlockItem(itemSpawnPos, fState.Output.Type);
								}
								_world.Furnaces.Remove(blockPos);
							}
						}
						else if (hitBlock == BlockType.Leaves)
						{
							// Leaves drop Apple with 1% chance
							if (Random.Shared.NextDouble() < 0.01)
							{
								_world.SpawnBlockItem(itemSpawnPos, BlockType.Apple);
							}
						}
						else if (hitBlock == BlockType.Grass)
						{
							// Grass drops Dirt always
							_world.SpawnBlockItem(itemSpawnPos, BlockType.Dirt);
							// 1.5% chance to drop Raw Porkchop
							if (Random.Shared.NextDouble() < 0.015)
							{
								_world.SpawnBlockItem(itemSpawnPos, BlockType.RawPorkchop);
							}
						}
						else if (BlockDatabase.CanDropItem(hitBlock, SelectedBlock))
						{
							_world.SpawnBlockItem(itemSpawnPos, hitBlock);
						}

						// Award XP for mining ores
						if (hitBlock == BlockType.CoalOre || hitBlock == BlockType.IronOre || hitBlock == BlockType.GoldOre || hitBlock == BlockType.DiamondOre)
						{
							float xpReward = hitBlock switch
							{
								BlockType.CoalOre => 0.07f,
								BlockType.IronOre => 0.15f,
								BlockType.GoldOre => 0.3f,
								BlockType.DiamondOre => 0.6f,
								_ => 0f
							};
							AddXp(xpReward);
						}
						
						ResetMining();
					}
				}
				else
				{
					ResetMining();
				}
			}
			else
			{
				ResetMining();
			}
		}
		else
		{
			ResetMining();
		}
	}

	private void ResetMining()
	{
		_miningBlockPos = null;
		_miningProgress = 0f;
		_particleTimer = 0f;
		if (_miningProgressBar != null)
		{
			_miningProgressBar.Visible = false;
		}
		if (_camera != null)
		{
			_camera.HOffset = 0f;
			_camera.VOffset = 0f;
		}
		RemoveMiningDecal();
	}

	private float GetMiningTime(BlockType type, BlockType tool)
	{
		float baseTime = type switch
		{
			BlockType.Leaves => 0.15f,
			BlockType.Glass => 0.15f,
			BlockType.Grass => 0.35f,
			BlockType.Dirt => 0.35f,
			BlockType.Wood => 0.6f,
			BlockType.Planks => 0.6f,
			BlockType.Stone => 1.0f,
			BlockType.Cobblestone => 1.0f,
			BlockType.Brick => 1.0f,
			BlockType.CoalOre => 1.2f,
			BlockType.IronOre => 1.4f,
			BlockType.GoldOre => 1.6f,
			BlockType.DiamondOre => 1.8f,
			BlockType.CraftingTable => 0.6f,
			_ => 0.5f
		};

		float multiplier = BlockDatabase.GetMiningSpeedMultiplier(type, tool);
		return baseTime / multiplier;
	}

	private bool IsCollidingWithPlayer(Vector3I blockPos)
	{
		float px = Position.X;
		float py = Position.Y;
		float pz = Position.Z;

		float playerMinX = px - 0.4f;
		float playerMaxX = px + 0.4f;
		float playerMinY = py - 1.0f;
		float playerMaxY = py + 1.0f;
		float playerMinZ = pz - 0.4f;
		float playerMaxZ = pz + 0.4f;

		float blockMinX = blockPos.X;
		float blockMaxX = blockPos.X + 1.0f;
		float blockMinY = blockPos.Y;
		float blockMaxY = blockPos.Y + 1.0f;
		float blockMinZ = blockPos.Z;
		float blockMaxZ = blockPos.Z + 1.0f;

		return (playerMinX < blockMaxX && playerMaxX > blockMinX) &&
			   (playerMinY < blockMaxY && playerMaxY > blockMinY) &&
			   (playerMinZ < blockMaxZ && playerMaxZ > blockMinZ);
	}

	private void CreateHotbarUI()
	{
		var hudNode = GetNode<Control>("UI/HUD");
		
		// Kényszerítsük a HUD Control csomópontot teljes képernyősre, hogy a horgonyok megfelelően működjenek
		hudNode.AnchorLeft = 0f;
		hudNode.AnchorRight = 1f;
		hudNode.AnchorTop = 0f;
		hudNode.AnchorBottom = 1f;
		hudNode.OffsetLeft = 0;
		hudNode.OffsetRight = 0;
		hudNode.OffsetTop = 0;
		hudNode.OffsetBottom = 0;

		// Korábbi SelectedBlockLabel törlése
		if (hudNode.HasNode("SelectedBlockLabel"))
		{
			hudNode.GetNode("SelectedBlockLabel").QueueFree();
		}

		// Célkereszt (crosshair) hozzáadása a képernyő közepére
		var crosshair = new ColorRect();
		crosshair.CustomMinimumSize = new Vector2(4, 4);
		crosshair.Color = new Color(1, 1, 1, 0.8f);
		crosshair.AnchorLeft = 0.5f;
		crosshair.AnchorRight = 0.5f;
		crosshair.AnchorTop = 0.5f;
		crosshair.AnchorBottom = 0.5f;
		crosshair.OffsetLeft = -2;
		crosshair.OffsetRight = 2;
		crosshair.OffsetTop = -2;
		crosshair.OffsetBottom = 2;
		crosshair.GrowHorizontal = Control.GrowDirection.Both;
		crosshair.GrowVertical = Control.GrowDirection.Both;
		hudNode.AddChild(crosshair);

		// Bányászati folyamatjelző (progress bar) a célkereszt alatt
		_miningProgressBar = new ProgressBar();
		_miningProgressBar.CustomMinimumSize = new Vector2(60, 8);
		_miningProgressBar.ShowPercentage = false;
		_miningProgressBar.Visible = false;
		_miningProgressBar.AnchorLeft = 0.5f;
		_miningProgressBar.AnchorRight = 0.5f;
		_miningProgressBar.AnchorTop = 0.5f;
		_miningProgressBar.AnchorBottom = 0.5f;
		_miningProgressBar.OffsetLeft = -30;
		_miningProgressBar.OffsetRight = 30;
		_miningProgressBar.OffsetTop = 15;
		_miningProgressBar.OffsetBottom = 23;
		
		var pbBg = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f, 0.5f) };
		var pbFg = new StyleBoxFlat { BgColor = new Color(0.8f, 0.2f, 0.2f, 0.8f) };
		_miningProgressBar.AddThemeStyleboxOverride("background", pbBg);
		_miningProgressBar.AddThemeStyleboxOverride("fill", pbFg);
		hudNode.AddChild(_miningProgressBar);

		// Hotbar konténer létrehozása
		var hotbarContainer = new HBoxContainer();
		hotbarContainer.Name = "Hotbar";
		hotbarContainer.AddThemeConstantOverride("separation", 4);

		hotbarContainer.AnchorLeft = 0.5f;
		hotbarContainer.AnchorRight = 0.5f;
		hotbarContainer.AnchorTop = 1.0f;
		hotbarContainer.AnchorBottom = 1.0f;
		hotbarContainer.OffsetLeft = -286;
		hotbarContainer.OffsetRight = 286;
		hotbarContainer.OffsetTop = -75;
		hotbarContainer.OffsetBottom = -15;
		hotbarContainer.GrowHorizontal = Control.GrowDirection.Both;
		hotbarContainer.GrowVertical = Control.GrowDirection.Begin;

		_hotbarSlots = new TextureRect[_hotbarBlocks.Length];

		var slotFrameTex = GD.Load<Texture2D>("res://Tilemap/hotbar_one_frame16x16.png");
		var terrainTex = GD.Load<Texture2D>("res://Tilemap/terrain.png");

		for (int i = 0; i < _hotbarBlocks.Length; i++)
		{
			// Slot keret TextureRect
			var slotFrame = new TextureRect();
			slotFrame.Texture = slotFrameTex;
			slotFrame.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			slotFrame.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			slotFrame.StretchMode = TextureRect.StretchModeEnum.Scale;
			slotFrame.CustomMinimumSize = new Vector2(60, 60);

			// Blokk 2D oldalnézet TextureRect
			var blockPreview = new TextureRect();
			
			var atlasTex = new AtlasTexture();
			atlasTex.Atlas = terrainTex;
			
			// Lekérjük a blokk oldalának textúra indexét (face index 2 = Side)
			int texIndex = BlockDatabase.GetTextureIndex(_hotbarBlocks[i].Type, 2);
			int col = texIndex % BlockDatabase.AtlasCols;
			int row = texIndex / BlockDatabase.AtlasCols;
			atlasTex.Region = new Rect2(col * 32, row * 32, 32, 32);

			blockPreview.Texture = atlasTex;
			blockPreview.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			blockPreview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			blockPreview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			blockPreview.CustomMinimumSize = new Vector2(40, 40);
			
			// Középre igazítjuk a kereten belül (egyszerű fix pozícióval és mérettel)
			blockPreview.Position = new Vector2(10, 10);
			blockPreview.Size = new Vector2(40, 40);
			
			slotFrame.AddChild(blockPreview);

			// Gomb gyorsbillentyű felirat
			var keyLabel = new Label();
			keyLabel.Text = (i + 1).ToString();
			keyLabel.AddThemeFontSizeOverride("font_size", 10);
			keyLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f, 0.9f));
			keyLabel.Position = new Vector2(5, 3);
			slotFrame.AddChild(keyLabel);

			// Darabszám felirat
			var countLabel = new Label();
			countLabel.Name = "CountLabel";
			countLabel.Text = "";
			countLabel.AddThemeFontSizeOverride("font_size", 12);
			countLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			countLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
			countLabel.AddThemeConstantOverride("shadow_offset_x", 1);
			countLabel.AddThemeConstantOverride("shadow_offset_y", 1);
			countLabel.AnchorLeft = 1.0f;
			countLabel.AnchorTop = 1.0f;
			countLabel.AnchorRight = 1.0f;
			countLabel.AnchorBottom = 1.0f;
			countLabel.OffsetLeft = -25;
			countLabel.OffsetTop = -20;
			countLabel.OffsetRight = -5;
			countLabel.OffsetBottom = -2;
			countLabel.HorizontalAlignment = HorizontalAlignment.Right;
			slotFrame.AddChild(countLabel);

			hotbarContainer.AddChild(slotFrame);
			_hotbarSlots[i] = slotFrame;
		}

		hudNode.AddChild(hotbarContainer);
		UpdateHotbarSelection();
	}

	private void UpdateHotbarSelection()
	{
		for (int i = 0; i < _hotbarSlots.Length; i++)
		{
			if (i == _selectedHotbarIndex)
			{
				// Kiválasztott slot frame: világos keret
				_hotbarSlots[i].SelfModulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			}
			else
			{
				// Nem kiválasztott slot frame: sötétebb, átlátszóbb
				_hotbarSlots[i].SelfModulate = new Color(0.5f, 0.5f, 0.5f, 0.75f);
			}
		}
	}

	private string GetFriendlyBlockName(BlockType type)
	{
		return type switch
		{
			BlockType.Grass => "Fű",
			BlockType.Dirt => "Föld",
			BlockType.Stone => "Kő",
			BlockType.Wood => "Fa",
			BlockType.Leaves => "Lomb",
			BlockType.Brick => "Tégla",
			BlockType.Planks => "Deszka",
			BlockType.Glass => "Üveg",
			BlockType.Cobblestone => "Zúzottkő",
			BlockType.CoalOre => "Szén",
			BlockType.IronOre => "Vas",
			BlockType.GoldOre => "Arany",
			BlockType.DiamondOre => "Gyémánt",
			_ => type.ToString()
		};
	}

	public void PlayPickupSound()
	{
		GD.Print("Játékos felvett egy tárgyat!");
	}

	private void UpdateMiningDecal(Vector3I blockPos, float progress)
	{
		if (_breakingTexture == null)
		{
			_breakingTexture = GD.Load<Texture2D>("res://Tilemap/braking_128x32.png");
		}

		if (_miningOverlay == null)
		{
			_miningOverlay = new MeshInstance3D();
			
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(1.002f, 1.002f, 1.002f);
			_miningOverlay.Mesh = boxMesh;

			var mat = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
				Roughness = 1.0f,
				Metallic = 0.0f,
				MetallicSpecular = 0.0f,
				AlbedoTexture = _breakingTexture,
				Uv1Scale = new Vector3(0.25f, 1.0f, 1.0f),
				CullMode = BaseMaterial3D.CullModeEnum.Back
			};
			
			_miningOverlay.MaterialOverride = mat;
			_world.AddChild(_miningOverlay);
		}

		_miningOverlay.Position = new Vector3(blockPos.X + 0.5f, blockPos.Y + 0.5f, blockPos.Z + 0.5f);

		int frameIndex = Mathf.Clamp(Mathf.FloorToInt(progress * 4f), 0, 3);
		
		var matInstance = (StandardMaterial3D)_miningOverlay.MaterialOverride;
		matInstance.Uv1Offset = new Vector3(frameIndex * 0.25f, 0.0f, 0.0f);
		
		_miningOverlay.Visible = true;
	}

	private void RemoveMiningDecal()
	{
		if (_miningOverlay != null)
		{
			_miningOverlay.QueueFree();
			_miningOverlay = null!;
		}
	}

	public override void _Process(double delta)
	{
		if (_isInventoryOpen && !_cursorItem.IsEmpty)
		{
			_cursorIcon.GlobalPosition = GetViewport().GetMousePosition() - new Vector2(16, 16);
		}
	}

	public static bool IsTool(BlockType type)
	{
		string name = type.ToString();
		return name.Contains("Pickaxe") || name.Contains("Axe") || name.Contains("Shovel") || name.Contains("Sword") || name.Contains("Hoe");
	}

	public static int GetItemTextureIndex(BlockType type)
	{
		return type switch
		{
			BlockType.Stick => 0,
			
			BlockType.WoodenSword => 1,
			BlockType.WoodenPickaxe => 2,
			BlockType.WoodenAxe => 3,
			BlockType.WoodenHoe => 4,
			BlockType.WoodenShovel => 5,
			
			BlockType.StoneSword => 6,
			BlockType.StonePickaxe => 7,
			BlockType.StoneAxe => 8,
			BlockType.StoneHoe => 9,
			BlockType.StoneShovel => 10,
			
			BlockType.IronSword => 11,
			BlockType.IronPickaxe => 12,
			BlockType.IronAxe => 13,
			BlockType.IronHoe => 14,
			BlockType.IronShovel => 15,
			
			BlockType.GoldSword => 16,
			BlockType.GoldPickaxe => 17,
			BlockType.GoldAxe => 18,
			BlockType.GoldHoe => 19,
			BlockType.GoldShovel => 20,
			
			BlockType.DiamondSword => 21,
			BlockType.DiamondPickaxe => 22,
			BlockType.DiamondAxe => 23,
			BlockType.DiamondHoe => 24,
			BlockType.DiamondShovel => 25,
			
			BlockType.Apple => 26,
			BlockType.RawPorkchop => 27,
			BlockType.CookedPorkchop => 28,
			
			_ => -1
		};
	}

	private void SetTextureForItem(TextureRect rect, BlockType type)
	{
		int itemIndex = GetItemTextureIndex(type);
		if (itemIndex >= 0)
		{
			AtlasTexture atlasTex;
			if (rect.Texture is AtlasTexture at && at.Atlas != null && at.Atlas.ResourcePath.Contains("items_256x32"))
			{
				atlasTex = at;
			}
			else
			{
				atlasTex = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://Tilemap/items_256x32.png") };
				rect.Texture = atlasTex;
			}
			
			int col = itemIndex % 16;
			int row = itemIndex / 16;
			atlasTex.Region = new Rect2(col * 16, row * 16, 16, 16);
		}
		else
		{
			AtlasTexture atlasTex;
			if (rect.Texture is AtlasTexture at && at.Atlas != null && at.Atlas.ResourcePath.Contains("terrain"))
			{
				atlasTex = at;
			}
			else
			{
				atlasTex = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://Tilemap/terrain.png") };
				rect.Texture = atlasTex;
			}
			
			int texIndex = BlockDatabase.GetTextureIndex(type, 2);
			int col = texIndex % BlockDatabase.AtlasCols;
			int row = texIndex / BlockDatabase.AtlasCols;
			atlasTex.Region = new Rect2(col * 32, row * 32, 32, 32);
		}
	}

	private void CreateInventoryUI()
	{
		var uiNode = GetNode<CanvasLayer>("UI");
		
		_inventoryMenu = new Control();
		_inventoryMenu.Name = "InventoryMenu";
		_inventoryMenu.Visible = false;
		_inventoryMenu.AnchorLeft = 0f;
		_inventoryMenu.AnchorRight = 1f;
		_inventoryMenu.AnchorTop = 0f;
		_inventoryMenu.AnchorBottom = 1f;
		_inventoryMenu.OffsetLeft = 0;
		_inventoryMenu.OffsetRight = 0;
		_inventoryMenu.OffsetTop = 0;
		_inventoryMenu.OffsetBottom = 0;
		uiNode.AddChild(_inventoryMenu);

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0, 0, 0, 0.5f);
		bgOverlay.AnchorLeft = 0f;
		bgOverlay.AnchorRight = 1f;
		bgOverlay.AnchorTop = 0f;
		bgOverlay.AnchorBottom = 1f;
		bgOverlay.OffsetLeft = 0;
		bgOverlay.OffsetRight = 0;
		bgOverlay.OffsetTop = 0;
		bgOverlay.OffsetBottom = 0;
		_inventoryMenu.AddChild(bgOverlay);

		_inventoryPanel = new TextureRect();
		_inventoryPanel.Texture = GD.Load<Texture2D>("res://Tilemap/inventory_128x128.png");
		_inventoryPanel.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		_inventoryPanel.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_inventoryPanel.StretchMode = TextureRect.StretchModeEnum.Scale;
		
		int scale = 5;
		_inventoryPanel.CustomMinimumSize = new Vector2(128 * scale, 128 * scale);
		_inventoryPanel.AnchorLeft = 0.5f;
		_inventoryPanel.AnchorRight = 0.5f;
		_inventoryPanel.AnchorTop = 0.5f;
		_inventoryPanel.AnchorBottom = 0.5f;
		_inventoryPanel.GrowHorizontal = Control.GrowDirection.Both;
		_inventoryPanel.GrowVertical = Control.GrowDirection.Both;
		_inventoryPanel.OffsetLeft = -64 * scale;
		_inventoryPanel.OffsetRight = 64 * scale;
		_inventoryPanel.OffsetTop = -64 * scale;
		_inventoryPanel.OffsetBottom = 64 * scale;
		_inventoryMenu.AddChild(_inventoryPanel);

		void CreateSlot(int index, bool isHotbar, bool isArmor, bool isCrafting, bool isCraftingResult, float px, float py, float pw, float ph, bool isTableCrafting = false, bool isTableCraftingResult = false, bool isFurnaceInput = false, bool isFurnaceFuel = false, bool isFurnaceOutput = false)
		{
			var slotControl = new Control();
			slotControl.CustomMinimumSize = new Vector2(pw * scale, ph * scale);
			slotControl.Position = new Vector2(px * scale, py * scale);
			_inventoryPanel.AddChild(slotControl);

			var hoverRect = new ColorRect();
			hoverRect.Color = new Color(1, 1, 1, 0.15f);
			hoverRect.Visible = false;
			hoverRect.AnchorLeft = 0f;
			hoverRect.AnchorRight = 1f;
			hoverRect.AnchorTop = 0f;
			hoverRect.AnchorBottom = 1f;
			hoverRect.OffsetLeft = 0;
			hoverRect.OffsetRight = 0;
			hoverRect.OffsetTop = 0;
			hoverRect.OffsetBottom = 0;
			hoverRect.MouseFilter = Control.MouseFilterEnum.Ignore;
			slotControl.AddChild(hoverRect);

			var icon = new TextureRect();
			icon.Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://Tilemap/terrain.png") };
			icon.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			
			icon.AnchorLeft = 0.5f;
			icon.AnchorRight = 0.5f;
			icon.AnchorTop = 0.5f;
			icon.AnchorBottom = 0.5f;
			float iconSize = Mathf.Min(pw, ph) * 0.8f;
			icon.OffsetLeft = -iconSize * scale * 0.5f;
			icon.OffsetRight = iconSize * scale * 0.5f;
			icon.OffsetTop = -iconSize * scale * 0.5f;
			icon.OffsetBottom = iconSize * scale * 0.5f;
			icon.MouseFilter = Control.MouseFilterEnum.Ignore;
			slotControl.AddChild(icon);

			var countLabel = new Label();
			countLabel.Name = "CountLabel";
			countLabel.Text = "";
			countLabel.AddThemeFontSizeOverride("font_size", 12);
			countLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			countLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
			countLabel.AddThemeConstantOverride("shadow_offset_x", 1);
			countLabel.AddThemeConstantOverride("shadow_offset_y", 1);
			countLabel.AnchorLeft = 1.0f;
			countLabel.AnchorTop = 1.0f;
			countLabel.AnchorRight = 1.0f;
			countLabel.AnchorBottom = 1.0f;
			countLabel.OffsetLeft = -25;
			countLabel.OffsetTop = -18;
			countLabel.OffsetRight = -2;
			countLabel.OffsetBottom = -2;
			countLabel.HorizontalAlignment = HorizontalAlignment.Right;
			countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			slotControl.AddChild(countLabel);

			var slotObj = new InventorySlot
			{
				Index = index,
				IsHotbar = isHotbar,
				IsArmor = isArmor,
				IsCrafting = isCrafting,
				IsCraftingResult = isCraftingResult,
				IsTableCrafting = isTableCrafting,
				IsTableCraftingResult = isTableCraftingResult,
				IsFurnaceInput = isFurnaceInput,
				IsFurnaceFuel = isFurnaceFuel,
				IsFurnaceOutput = isFurnaceOutput,
				GuiControl = slotControl,
				IconRect = icon,
				CountLabel = countLabel
			};

			_inventorySlotsList.Add(slotObj);

			slotControl.MouseEntered += () => hoverRect.Visible = true;
			slotControl.MouseExited += () => hoverRect.Visible = false;
			slotControl.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
				{
					if (mouseButton.ButtonIndex == MouseButton.Left)
					{
						bool isShiftPressed = Input.IsKeyPressed(Key.Shift);
						OnSlotClicked(slotObj, isLeft: true, isShiftPressed: isShiftPressed);
					}
					else if (mouseButton.ButtonIndex == MouseButton.Right)
					{
						OnSlotClicked(slotObj, isLeft: false, isShiftPressed: false);
					}
				}
			};
		}

		// 1. Armor Slots (4db)
		CreateSlot(0, false, true, false, false, 24, 23, 8, 7);
		CreateSlot(1, false, true, false, false, 24, 31, 8, 7);
		CreateSlot(2, false, true, false, false, 24, 39, 8, 7);
		CreateSlot(3, false, true, false, false, 24, 47, 8, 6);

		// 2. Crafting Slots (4db, 2x2)
		CreateSlot(0, false, false, true, false, 62, 23, 13, 14);
		CreateSlot(1, false, false, true, false, 76, 23, 15, 14);
		CreateSlot(2, false, false, true, false, 62, 38, 13, 15);
		CreateSlot(3, false, false, true, false, 76, 38, 15, 15);

		// 3. Crafting Result (1db)
		CreateSlot(0, false, false, false, true, 98, 34, 8, 8);

		// 4. Table Crafting Slots (9db, 3x3)
		for (int r = 0; r < 3; r++)
		{
			float y = 25 + r * 8;
			for (int c = 0; c < 3; c++)
			{
				float x = 62 + c * 9;
				CreateSlot(r * 3 + c, false, false, false, false, x, y, 8, 7, isTableCrafting: true);
			}
		}

		// 5. Table Crafting Result (1db)
		CreateSlot(0, false, false, false, false, 98, 34, 8, 8, isTableCraftingResult: true);

		// 6. Inventory Rows (4 sor x 9 oszlop)
		for (int r = 0; r < 4; r++)
		{
			float y = 64 + r * 8;
			for (int c = 0; c < 9; c++)
			{
				float x = 25 + c * 9;
				CreateSlot(r * 9 + c, false, false, false, false, x, y, 8, 7);
			}
		}

		// 7. Hotbar Row (1 sor x 9 oszlop)
		for (int c = 0; c < 9; c++)
		{
			float x = 25 + c * 9;
			CreateSlot(c, true, false, false, false, x, 98, 8, 7);
		}

		// 8. Furnace Slots (3db)
		CreateSlot(0, false, false, false, false, 45, 28, 8, 7, isFurnaceInput: true);
		CreateSlot(0, false, false, false, false, 45, 45, 8, 7, isFurnaceFuel: true);
		CreateSlot(0, false, false, false, false, 74, 37, 8, 8, isFurnaceOutput: true);

		// 8. Lebegő kurzor ikon
		_cursorIcon = new TextureRect();
		_cursorIcon.Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://Tilemap/terrain.png") };
		_cursorIcon.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		_cursorIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_cursorIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_cursorIcon.CustomMinimumSize = new Vector2(32, 32);
		_cursorIcon.Size = new Vector2(32, 32);
		_cursorIcon.Visible = false;
		_cursorIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
		_inventoryMenu.AddChild(_cursorIcon);

		RefreshInventoryUI();
	}

	private void OpenCraftingTableMenu()
	{
		_isCraftingTableOpen = true;
		_isFurnaceOpen = false;
		_isInventoryOpen = true;
		_inventoryMenu.Visible = true;
		_inventoryPanel.Texture = GD.Load<Texture2D>("res://Tilemap/inventory_128x128.png");
		Input.MouseMode = Input.MouseModeEnum.Visible;
		RefreshInventoryUI();
	}

	private void OpenFurnaceMenu(Vector3I targetPos)
	{
		_isFurnaceOpen = true;
		_openFurnacePos = targetPos;
		_isCraftingTableOpen = false;
		_isInventoryOpen = true;
		_inventoryMenu.Visible = true;
		_inventoryPanel.Texture = GD.Load<Texture2D>("res://Tilemap/furnace_128x128.png");
		Input.MouseMode = Input.MouseModeEnum.Visible;
		RefreshInventoryUI();
	}

	private void ToggleInventory()
	{
		_isInventoryOpen = !_isInventoryOpen;
		_inventoryMenu.Visible = _isInventoryOpen;

		if (_isInventoryOpen)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_inventoryPanel.Texture = GD.Load<Texture2D>("res://Tilemap/inventory_128x128.png");
			RefreshInventoryUI();
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			ReturnCraftingIngredients();
			_isCraftingTableOpen = false;
			_isFurnaceOpen = false;

			if (!_cursorItem.IsEmpty)
			{
				AddItemStackToInventory(ref _cursorItem, hotbarOnly: false, mainInventoryOnly: false);
				_cursorItem = ItemStack.Empty;
			}
		}
	}

	private void ReturnCraftingIngredients()
	{
		for (int i = 0; i < 4; i++)
		{
			if (!_craftingBlocks[i].IsEmpty)
			{
				AddItemStackToInventory(ref _craftingBlocks[i], hotbarOnly: false, mainInventoryOnly: false);
				_craftingBlocks[i] = ItemStack.Empty;
			}
		}
		_craftingResultBlock = ItemStack.Empty;

		for (int i = 0; i < 9; i++)
		{
			if (!_tableCraftingBlocks[i].IsEmpty)
			{
				AddItemStackToInventory(ref _tableCraftingBlocks[i], hotbarOnly: false, mainInventoryOnly: false);
				_tableCraftingBlocks[i] = ItemStack.Empty;
			}
		}
		_tableCraftingResultBlock = ItemStack.Empty;
	}

	private ItemStack GetSlotItem(InventorySlot slot)
	{
		if (slot.IsHotbar) return _hotbarBlocks[slot.Index];
		if (slot.IsArmor) return _armorBlocks[slot.Index];
		if (slot.IsCrafting) return _craftingBlocks[slot.Index];
		if (slot.IsCraftingResult) return _craftingResultBlock;
		if (slot.IsTableCrafting) return _tableCraftingBlocks[slot.Index];
		if (slot.IsTableCraftingResult) return _tableCraftingResultBlock;
		if (slot.IsFurnaceInput || slot.IsFurnaceFuel || slot.IsFurnaceOutput)
		{
			if (_world.Furnaces.TryGetValue(_openFurnacePos, out var fState))
			{
				if (slot.IsFurnaceInput) return fState.Input;
				if (slot.IsFurnaceFuel) return fState.Fuel;
				if (slot.IsFurnaceOutput) return fState.Output;
			}
			return ItemStack.Empty;
		}
		return _inventoryBlocks[slot.Index];
	}

	private void SetSlotItem(InventorySlot slot, ItemStack item)
	{
		if (slot.IsHotbar) _hotbarBlocks[slot.Index] = item;
		else if (slot.IsArmor) _armorBlocks[slot.Index] = item;
		else if (slot.IsCrafting) _craftingBlocks[slot.Index] = item;
		else if (slot.IsCraftingResult) _craftingResultBlock = item;
		else if (slot.IsTableCrafting) _tableCraftingBlocks[slot.Index] = item;
		else if (slot.IsTableCraftingResult) _tableCraftingResultBlock = item;
		else if (slot.IsFurnaceInput || slot.IsFurnaceFuel || slot.IsFurnaceOutput)
		{
			if (!_world.Furnaces.TryGetValue(_openFurnacePos, out var fState))
			{
				if (_world.GetBlockAtGlobal(_openFurnacePos) == BlockType.Furnace)
				{
					fState = new FurnaceState();
					_world.Furnaces[_openFurnacePos] = fState;
				}
				else
				{
					return;
				}
			}
			if (slot.IsFurnaceInput) fState.Input = item;
			else if (slot.IsFurnaceFuel) fState.Fuel = item;
			else if (slot.IsFurnaceOutput) fState.Output = item;
		}
		else _inventoryBlocks[slot.Index] = item;
	}

	private void OnSlotClicked(InventorySlot slot, bool isLeft, bool isShiftPressed)
	{
		if (isShiftPressed)
		{
			if (slot.IsCraftingResult || slot.IsTableCraftingResult || slot.IsFurnaceOutput)
			{
				ItemStack result = GetSlotItem(slot);
				if (result.IsEmpty) return;

				ItemStack toTransfer = result;
				bool changed = AddItemStackToInventory(ref toTransfer, hotbarOnly: false, mainInventoryOnly: false);
				if (changed)
				{
					int transferredCount = result.Count - toTransfer.Count;
					if (transferredCount > 0)
					{
						if (slot.IsCraftingResult)
						{
							ConsumeCraftingIngredients2x2();
							UpdateCraftingRecipe();
						}
						else if (slot.IsTableCraftingResult)
						{
							ConsumeCraftingIngredients3x3();
							UpdateTableCraftingRecipe();
						}
						else // IsFurnaceOutput
						{
							SetSlotItem(slot, toTransfer);
						}
						RefreshInventoryUI();
						UpdateHotbarIcons();
					}
				}
				return;
			}

			if (_isFurnaceOpen)
			{
				ItemStack stack = GetSlotItem(slot);
				if (stack.IsEmpty) return;

				if (slot.IsFurnaceInput || slot.IsFurnaceFuel)
				{
					// Transfer furnace slot item to inventory
					bool changed = AddItemStackToInventory(ref stack, hotbarOnly: false, mainInventoryOnly: false);
					if (changed)
					{
						SetSlotItem(slot, stack);
						RefreshInventoryUI();
						UpdateHotbarIcons();
					}
					return;
				}
				else
				{
					// Shift-clicking from inventory/hotbar into furnace
					bool changed = false;

					// 1. Try to put in Fuel slot if it is a valid fuel
					bool isValidFuel = World.GetFuelBurnTime(stack.Type) > 0;
					if (isValidFuel)
					{
						var fuelSlot = _inventorySlotsList.Find(s => s.IsFurnaceFuel);
						if (fuelSlot != null)
						{
							ItemStack fuelStack = GetSlotItem(fuelSlot);
							if (fuelStack.IsEmpty)
							{
								SetSlotItem(fuelSlot, stack);
								stack = ItemStack.Empty;
								changed = true;
							}
							else if (fuelStack.Type == stack.Type && fuelStack.Count < 64)
							{
								int transfer = Math.Min(64 - fuelStack.Count, stack.Count);
								fuelStack.Count += transfer;
								stack.Count -= transfer;
								SetSlotItem(fuelSlot, fuelStack);
								changed = true;
							}
						}
					}

					// 2. If stack is not empty, try to put in Input slot if it's smeltable
					if (!stack.IsEmpty && World.GetSmeltResult(stack.Type) != BlockType.Air)
					{
						var inputSlot = _inventorySlotsList.Find(s => s.IsFurnaceInput);
						if (inputSlot != null)
						{
							ItemStack inputStack = GetSlotItem(inputSlot);
							if (inputStack.IsEmpty)
							{
								SetSlotItem(inputSlot, stack);
								stack = ItemStack.Empty;
								changed = true;
							}
							else if (inputStack.Type == stack.Type && inputStack.Count < 64)
							{
								int transfer = Math.Min(64 - inputStack.Count, stack.Count);
								inputStack.Count += transfer;
								stack.Count -= transfer;
								SetSlotItem(inputSlot, inputStack);
								changed = true;
							}
						}
					}

					// 3. Standard hotbar/inventory swap if not fully transferred
					if (!stack.IsEmpty && !changed)
					{
						if (slot.IsHotbar)
						{
							changed = AddItemStackToInventory(ref stack, hotbarOnly: false, mainInventoryOnly: true);
						}
						else
						{
							changed = AddItemStackToInventory(ref stack, hotbarOnly: true, mainInventoryOnly: false);
							if (!stack.IsEmpty)
							{
								changed |= AddItemStackToInventory(ref stack, hotbarOnly: false, mainInventoryOnly: true);
							}
						}
					}

					if (changed)
					{
						SetSlotItem(slot, stack);
						RefreshInventoryUI();
						UpdateHotbarIcons();
					}
					return;
				}
			}

			ItemStack stackToSwap = GetSlotItem(slot);
			if (stackToSwap.IsEmpty) return;

			bool changedTransfer = false;
			if (slot.IsHotbar)
			{
				changedTransfer = AddItemStackToInventory(ref stackToSwap, hotbarOnly: false, mainInventoryOnly: true);
			}
			else
			{
				changedTransfer = AddItemStackToInventory(ref stackToSwap, hotbarOnly: true, mainInventoryOnly: false);
				if (!stackToSwap.IsEmpty)
				{
					changedTransfer |= AddItemStackToInventory(ref stackToSwap, hotbarOnly: false, mainInventoryOnly: true);
				}
			}

			if (changedTransfer)
			{
				SetSlotItem(slot, stackToSwap);
				if (slot.IsCrafting) UpdateCraftingRecipe();
				if (slot.IsTableCrafting) UpdateTableCraftingRecipe();
				UpdateHotbarIcons();
				RefreshInventoryUI();
			}
			return;
		}

		if (slot.IsCraftingResult)
		{
			if (_cursorItem.IsEmpty && !_craftingResultBlock.IsEmpty)
			{
				_cursorItem = _craftingResultBlock;
				_craftingResultBlock = ItemStack.Empty;
				ConsumeCraftingIngredients2x2();
				RefreshInventoryUI();
			}
			else if (!_cursorItem.IsEmpty && _cursorItem.Type == _craftingResultBlock.Type && _cursorItem.Count + _craftingResultBlock.Count <= 64)
			{
				_cursorItem.Count += _craftingResultBlock.Count;
				_craftingResultBlock = ItemStack.Empty;
				ConsumeCraftingIngredients2x2();
				RefreshInventoryUI();
			}
			return;
		}

		if (slot.IsTableCraftingResult)
		{
			if (_cursorItem.IsEmpty && !_tableCraftingResultBlock.IsEmpty)
			{
				_cursorItem = _tableCraftingResultBlock;
				_tableCraftingResultBlock = ItemStack.Empty;
				ConsumeCraftingIngredients3x3();
				RefreshInventoryUI();
			}
			else if (!_cursorItem.IsEmpty && _cursorItem.Type == _tableCraftingResultBlock.Type && _cursorItem.Count + _tableCraftingResultBlock.Count <= 64)
			{
				_cursorItem.Count += _tableCraftingResultBlock.Count;
				_tableCraftingResultBlock = ItemStack.Empty;
				ConsumeCraftingIngredients3x3();
				RefreshInventoryUI();
			}
			return;
		}

		if (slot.IsFurnaceOutput)
		{
			ItemStack fOutput = GetSlotItem(slot);
			if (fOutput.IsEmpty) return;

			if (_cursorItem.IsEmpty)
			{
				_cursorItem = fOutput;
				SetSlotItem(slot, ItemStack.Empty);
				RefreshInventoryUI();
			}
			else if (_cursorItem.Type == fOutput.Type && _cursorItem.Count + fOutput.Count <= 64)
			{
				_cursorItem.Count += fOutput.Count;
				SetSlotItem(slot, ItemStack.Empty);
				RefreshInventoryUI();
			}
			return;
		}

		ItemStack slotItem = GetSlotItem(slot);

		if (isLeft)
		{
			if (_cursorItem.IsEmpty)
			{
				if (!slotItem.IsEmpty)
				{
					_cursorItem = slotItem;
					SetSlotItem(slot, ItemStack.Empty);
				}
			}
			else
			{
				if (slotItem.IsEmpty)
				{
					SetSlotItem(slot, _cursorItem);
					_cursorItem = ItemStack.Empty;
				}
				else
				{
					if (slotItem.Type == _cursorItem.Type)
					{
						if (IsTool(slotItem.Type))
						{
							var tempTool = slotItem;
							SetSlotItem(slot, _cursorItem);
							_cursorItem = tempTool;
						}
						else
						{
							int space = 64 - slotItem.Count;
							int transfer = Math.Min(space, _cursorItem.Count);
							slotItem.Count += transfer;
							_cursorItem.Count -= transfer;
							if (_cursorItem.Count <= 0) _cursorItem = ItemStack.Empty;
							SetSlotItem(slot, slotItem);
						}
					}
					else
					{
						var tempDiff = slotItem;
						SetSlotItem(slot, _cursorItem);
						_cursorItem = tempDiff;
					}
				}
			}
		}
		else
		{
			if (_cursorItem.IsEmpty)
			{
				if (!slotItem.IsEmpty)
				{
					int take = (slotItem.Count + 1) / 2;
					_cursorItem = new ItemStack(slotItem.Type, take);
					slotItem.Count -= take;
					if (slotItem.Count <= 0) slotItem = ItemStack.Empty;
					SetSlotItem(slot, slotItem);
				}
			}
			else
			{
				if (slotItem.IsEmpty)
				{
					SetSlotItem(slot, new ItemStack(_cursorItem.Type, 1));
					_cursorItem.Count -= 1;
					if (_cursorItem.Count <= 0) _cursorItem = ItemStack.Empty;
				}
				else
				{
					if (slotItem.Type == _cursorItem.Type)
					{
						if (!IsTool(slotItem.Type) && slotItem.Count < 64)
						{
							slotItem.Count += 1;
							_cursorItem.Count -= 1;
							if (_cursorItem.Count <= 0) _cursorItem = ItemStack.Empty;
							SetSlotItem(slot, slotItem);
						}
					}
					else
					{
						var tempDiff = slotItem;
						SetSlotItem(slot, _cursorItem);
						_cursorItem = tempDiff;
					}
				}
			}
		}

		if (slot.IsHotbar) UpdateHotbarIcons();
		if (slot.IsCrafting) UpdateCraftingRecipe();
		if (slot.IsTableCrafting) UpdateTableCraftingRecipe();

		RefreshInventoryUI();
	}

	private bool AddItemStackToInventory(ref ItemStack stack, bool hotbarOnly, bool mainInventoryOnly)
	{
		if (stack.IsEmpty) return false;
		bool changed = false;

		if (!IsTool(stack.Type))
		{
			if (!mainInventoryOnly)
			{
				for (int i = 0; i < _hotbarBlocks.Length; i++)
				{
					if (!_hotbarBlocks[i].IsEmpty && _hotbarBlocks[i].Type == stack.Type && _hotbarBlocks[i].Count < 64)
					{
						int space = 64 - _hotbarBlocks[i].Count;
						int transfer = Math.Min(space, stack.Count);
						_hotbarBlocks[i].Count += transfer;
						stack.Count -= transfer;
						changed = true;
						if (stack.IsEmpty) return true;
					}
				}
			}

			if (!hotbarOnly)
			{
				for (int i = 0; i < _inventoryBlocks.Length; i++)
				{
					if (!_inventoryBlocks[i].IsEmpty && _inventoryBlocks[i].Type == stack.Type && _inventoryBlocks[i].Count < 64)
					{
						int space = 64 - _inventoryBlocks[i].Count;
						int transfer = Math.Min(space, stack.Count);
						_inventoryBlocks[i].Count += transfer;
						stack.Count -= transfer;
						changed = true;
						if (stack.IsEmpty) return true;
					}
				}
			}
		}

		if (!mainInventoryOnly)
		{
			for (int i = 0; i < _hotbarBlocks.Length; i++)
			{
				if (_hotbarBlocks[i].IsEmpty)
				{
					int maxCount = IsTool(stack.Type) ? 1 : 64;
					int transfer = Math.Min(maxCount, stack.Count);
					_hotbarBlocks[i] = new ItemStack(stack.Type, transfer);
					stack.Count -= transfer;
					changed = true;
					if (stack.IsEmpty) return true;
				}
			}
		}

		if (!hotbarOnly)
		{
			for (int i = 0; i < _inventoryBlocks.Length; i++)
			{
				if (_inventoryBlocks[i].IsEmpty)
				{
					int maxCount = IsTool(stack.Type) ? 1 : 64;
					int transfer = Math.Min(maxCount, stack.Count);
					_inventoryBlocks[i] = new ItemStack(stack.Type, transfer);
					stack.Count -= transfer;
					changed = true;
					if (stack.IsEmpty) return true;
				}
			}
		}

		return changed;
	}

	private void ConsumeCraftingIngredients2x2()
	{
		for (int i = 0; i < 4; i++)
		{
			if (!_craftingBlocks[i].IsEmpty)
			{
				var stack = _craftingBlocks[i];
				stack.Count -= 1;
				if (stack.Count <= 0)
				{
					_craftingBlocks[i] = ItemStack.Empty;
				}
				else
				{
					_craftingBlocks[i] = stack;
				}
			}
		}
		UpdateCraftingRecipe();
	}

	private void ConsumeCraftingIngredients3x3()
	{
		for (int i = 0; i < 9; i++)
		{
			if (!_tableCraftingBlocks[i].IsEmpty)
			{
				var stack = _tableCraftingBlocks[i];
				stack.Count -= 1;
				if (stack.Count <= 0)
				{
					_tableCraftingBlocks[i] = ItemStack.Empty;
				}
				else
				{
					_tableCraftingBlocks[i] = stack;
				}
			}
		}
		UpdateTableCraftingRecipe();
	}

	private BlockType GetToolForMaterial(string toolType, BlockType material)
	{
		return material switch
		{
			BlockType.Planks or BlockType.Wood => toolType switch
			{
				"pickaxe" => BlockType.WoodenPickaxe,
				"axe" => BlockType.WoodenAxe,
				"shovel" => BlockType.WoodenShovel,
				"sword" => BlockType.WoodenSword,
				"hoe" => BlockType.WoodenHoe,
				_ => BlockType.Air
			},
			BlockType.Cobblestone or BlockType.Stone => toolType switch
			{
				"pickaxe" => BlockType.StonePickaxe,
				"axe" => BlockType.StoneAxe,
				"shovel" => BlockType.StoneShovel,
				"sword" => BlockType.StoneSword,
				"hoe" => BlockType.StoneHoe,
				_ => BlockType.Air
			},
			BlockType.IronOre => toolType switch
			{
				"pickaxe" => BlockType.IronPickaxe,
				"axe" => BlockType.IronAxe,
				"shovel" => BlockType.IronShovel,
				"sword" => BlockType.IronSword,
				"hoe" => BlockType.IronHoe,
				_ => BlockType.Air
			},
			BlockType.GoldOre => toolType switch
			{
				"pickaxe" => BlockType.GoldPickaxe,
				"axe" => BlockType.GoldAxe,
				"shovel" => BlockType.GoldShovel,
				"sword" => BlockType.GoldSword,
				"hoe" => BlockType.GoldHoe,
				_ => BlockType.Air
			},
			BlockType.DiamondOre => toolType switch
			{
				"pickaxe" => BlockType.DiamondPickaxe,
				"axe" => BlockType.DiamondAxe,
				"shovel" => BlockType.DiamondShovel,
				"sword" => BlockType.DiamondSword,
				"hoe" => BlockType.DiamondHoe,
				_ => BlockType.Air
			},
			_ => BlockType.Air
		};
	}

	private void UpdateCraftingRecipe()
	{
		BlockType t0 = _craftingBlocks[0].Type;
		BlockType t1 = _craftingBlocks[1].Type;
		BlockType t2 = _craftingBlocks[2].Type;
		BlockType t3 = _craftingBlocks[3].Type;

		int nonAirCount = 0;
		BlockType singleType = BlockType.Air;

		for (int i = 0; i < 4; i++)
		{
			if (!_craftingBlocks[i].IsEmpty)
			{
				nonAirCount++;
				singleType = _craftingBlocks[i].Type;
			}
		}

		if (nonAirCount == 1 && singleType == BlockType.Wood)
		{
			_craftingResultBlock = new ItemStack(BlockType.Planks, 4);
			return;
		}

		if (nonAirCount == 4 && t0 == BlockType.Planks && t1 == BlockType.Planks && t2 == BlockType.Planks && t3 == BlockType.Planks)
		{
			_craftingResultBlock = new ItemStack(BlockType.CraftingTable, 1);
			return;
		}

		// Stick: 2 Planks vertically stacked
		if (nonAirCount == 2 && ((t0 == BlockType.Planks && t2 == BlockType.Planks) || (t1 == BlockType.Planks && t3 == BlockType.Planks)))
		{
			_craftingResultBlock = new ItemStack(BlockType.Stick, 4);
			return;
		}

		if (nonAirCount == 4 && t0 == BlockType.Cobblestone && t1 == BlockType.Cobblestone && t2 == BlockType.Cobblestone && t3 == BlockType.Cobblestone)
		{
			_craftingResultBlock = new ItemStack(BlockType.Stone, 4);
			return;
		}

		if (nonAirCount == 4 && t0 == BlockType.Stone && t1 == BlockType.Stone && t2 == BlockType.Stone && t3 == BlockType.Stone)
		{
			_craftingResultBlock = new ItemStack(BlockType.Brick, 4);
			return;
		}

		if (nonAirCount == 4 && t0 == BlockType.Dirt && t1 == BlockType.Dirt && t2 == BlockType.Dirt && t3 == BlockType.Dirt)
		{
			_craftingResultBlock = new ItemStack(BlockType.Grass, 4);
			return;
		}

		if (nonAirCount == 4 && t0 == BlockType.CoalOre && t1 == BlockType.CoalOre && t2 == BlockType.CoalOre && t3 == BlockType.CoalOre)
		{
			_craftingResultBlock = new ItemStack(BlockType.DiamondOre, 1);
			return;
		}

		_craftingResultBlock = ItemStack.Empty;
	}

	private void UpdateTableCraftingRecipe()
	{
		BlockType t0 = _tableCraftingBlocks[0].Type;
		BlockType t1 = _tableCraftingBlocks[1].Type;
		BlockType t2 = _tableCraftingBlocks[2].Type;
		BlockType t3 = _tableCraftingBlocks[3].Type;
		BlockType t4 = _tableCraftingBlocks[4].Type;
		BlockType t5 = _tableCraftingBlocks[5].Type;
		BlockType t6 = _tableCraftingBlocks[6].Type;
		BlockType t7 = _tableCraftingBlocks[7].Type;
		BlockType t8 = _tableCraftingBlocks[8].Type;

		int nonAirCount = 0;
		BlockType singleType = BlockType.Air;
		for (int i = 0; i < 9; i++)
		{
			if (!_tableCraftingBlocks[i].IsEmpty)
			{
				nonAirCount++;
				singleType = _tableCraftingBlocks[i].Type;
			}
		}

		if (nonAirCount == 1 && singleType == BlockType.Wood)
		{
			_tableCraftingResultBlock = new ItemStack(BlockType.Planks, 4);
			return;
		}

		if (nonAirCount == 4)
		{
			bool isPlanks2x2 = 
				(t0 == BlockType.Planks && t1 == BlockType.Planks && t3 == BlockType.Planks && t4 == BlockType.Planks) ||
				(t1 == BlockType.Planks && t2 == BlockType.Planks && t4 == BlockType.Planks && t5 == BlockType.Planks) ||
				(t3 == BlockType.Planks && t4 == BlockType.Planks && t6 == BlockType.Planks && t7 == BlockType.Planks) ||
				(t4 == BlockType.Planks && t5 == BlockType.Planks && t7 == BlockType.Planks && t8 == BlockType.Planks);

			if (isPlanks2x2)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.CraftingTable, 1);
				return;
			}

			bool isCobble2x2 = 
				(t0 == BlockType.Cobblestone && t1 == BlockType.Cobblestone && t3 == BlockType.Cobblestone && t4 == BlockType.Cobblestone) ||
				(t1 == BlockType.Cobblestone && t2 == BlockType.Cobblestone && t4 == BlockType.Cobblestone && t5 == BlockType.Cobblestone) ||
				(t3 == BlockType.Cobblestone && t4 == BlockType.Cobblestone && t6 == BlockType.Cobblestone && t7 == BlockType.Cobblestone) ||
				(t4 == BlockType.Cobblestone && t5 == BlockType.Cobblestone && t7 == BlockType.Cobblestone && t8 == BlockType.Cobblestone);
			if (isCobble2x2)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.Stone, 4);
				return;
			}

			bool isStone2x2 = 
				(t0 == BlockType.Stone && t1 == BlockType.Stone && t3 == BlockType.Stone && t4 == BlockType.Stone) ||
				(t1 == BlockType.Stone && t2 == BlockType.Stone && t4 == BlockType.Stone && t5 == BlockType.Stone) ||
				(t3 == BlockType.Stone && t4 == BlockType.Stone && t6 == BlockType.Stone && t7 == BlockType.Stone) ||
				(t4 == BlockType.Stone && t5 == BlockType.Stone && t7 == BlockType.Stone && t8 == BlockType.Stone);
			if (isStone2x2)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.Brick, 4);
				return;
			}

			bool isDirt2x2 = 
				(t0 == BlockType.Dirt && t1 == BlockType.Dirt && t3 == BlockType.Dirt && t4 == BlockType.Dirt) ||
				(t1 == BlockType.Dirt && t2 == BlockType.Dirt && t4 == BlockType.Dirt && t5 == BlockType.Dirt) ||
				(t3 == BlockType.Dirt && t4 == BlockType.Dirt && t6 == BlockType.Dirt && t7 == BlockType.Dirt) ||
				(t4 == BlockType.Dirt && t5 == BlockType.Dirt && t7 == BlockType.Dirt && t8 == BlockType.Dirt);
			if (isDirt2x2)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.Grass, 4);
				return;
			}

			bool isCoal2x2 = 
				(t0 == BlockType.CoalOre && t1 == BlockType.CoalOre && t3 == BlockType.CoalOre && t4 == BlockType.CoalOre) ||
				(t1 == BlockType.CoalOre && t2 == BlockType.CoalOre && t4 == BlockType.CoalOre && t5 == BlockType.CoalOre) ||
				(t3 == BlockType.CoalOre && t4 == BlockType.CoalOre && t6 == BlockType.CoalOre && t7 == BlockType.CoalOre) ||
				(t4 == BlockType.CoalOre && t5 == BlockType.CoalOre && t7 == BlockType.CoalOre && t8 == BlockType.CoalOre);
			if (isCoal2x2)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.DiamondOre, 1);
				return;
			}
		}

		// Stick: 2 Planks vertically stacked
		if (nonAirCount == 2)
		{
			bool isPlanksVert = 
				(t0 == BlockType.Planks && t3 == BlockType.Planks) ||
				(t3 == BlockType.Planks && t6 == BlockType.Planks) ||
				(t1 == BlockType.Planks && t4 == BlockType.Planks) ||
				(t4 == BlockType.Planks && t7 == BlockType.Planks) ||
				(t2 == BlockType.Planks && t5 == BlockType.Planks) ||
				(t5 == BlockType.Planks && t8 == BlockType.Planks);
			if (isPlanksVert)
			{
				_tableCraftingResultBlock = new ItemStack(BlockType.Stick, 4);
				return;
			}
		}

		// Furnace: 8 Cobblestone around outer ring
		if (nonAirCount == 8 && t4 == BlockType.Air &&
			t0 == BlockType.Cobblestone && t1 == BlockType.Cobblestone && t2 == BlockType.Cobblestone &&
			t3 == BlockType.Cobblestone &&                             t5 == BlockType.Cobblestone &&
			t6 == BlockType.Cobblestone && t7 == BlockType.Cobblestone && t8 == BlockType.Cobblestone)
		{
			_tableCraftingResultBlock = new ItemStack(BlockType.Furnace, 1);
			return;
		}

		// Sword: 2 Material vertically, 1 Stick below
		if (nonAirCount == 3)
		{
			BlockType mat = BlockType.Air;
			bool matchSword = false;
			
			// Col 0
			if (t0 != BlockType.Air && t0 == t3 && t6 == BlockType.Stick)
			{
				mat = t0;
				matchSword = true;
			}
			// Col 1
			else if (t1 != BlockType.Air && t1 == t4 && t7 == BlockType.Stick)
			{
				mat = t1;
				matchSword = true;
			}
			// Col 2
			else if (t2 != BlockType.Air && t2 == t5 && t8 == BlockType.Stick)
			{
				mat = t2;
				matchSword = true;
			}

			if (matchSword)
			{
				BlockType tool = GetToolForMaterial("sword", mat);
				if (tool != BlockType.Air)
				{
					_tableCraftingResultBlock = new ItemStack(tool, 1);
					return;
				}
			}
		}

		// Hoe: 2 Material, 2 Stick
		if (nonAirCount == 4)
		{
			BlockType mat = BlockType.Air;
			bool matchHoe = false;

			// Left-facing Col 1 handle
			if (t0 != BlockType.Air && t0 == t1 && t4 == BlockType.Stick && t7 == BlockType.Stick && t2 == BlockType.Air && t3 == BlockType.Air && t5 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t0;
				matchHoe = true;
			}
			// Left-facing Col 2 handle
			else if (t1 != BlockType.Air && t1 == t2 && t5 == BlockType.Stick && t8 == BlockType.Stick && t0 == BlockType.Air && t3 == BlockType.Air && t4 == BlockType.Air && t6 == BlockType.Air && t7 == BlockType.Air)
			{
				mat = t1;
				matchHoe = true;
			}
			// Right-facing Col 0 handle
			else if (t0 != BlockType.Air && t0 == t1 && t3 == BlockType.Stick && t6 == BlockType.Stick && t2 == BlockType.Air && t4 == BlockType.Air && t5 == BlockType.Air && t7 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t0;
				matchHoe = true;
			}
			// Right-facing Col 1 handle
			else if (t1 != BlockType.Air && t1 == t2 && t4 == BlockType.Stick && t7 == BlockType.Stick && t0 == BlockType.Air && t3 == BlockType.Air && t5 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t1;
				matchHoe = true;
			}

			if (matchHoe)
			{
				BlockType tool = GetToolForMaterial("hoe", mat);
				if (tool != BlockType.Air)
				{
					_tableCraftingResultBlock = new ItemStack(tool, 1);
					return;
				}
			}
		}

		// Pickaxe: 3 Material, 2 Stick (centered)
		if (nonAirCount == 5 && t0 != BlockType.Air && t0 == t1 && t1 == t2 && t4 == BlockType.Stick && t7 == BlockType.Stick &&
			t3 == BlockType.Air && t5 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
		{
			BlockType tool = GetToolForMaterial("pickaxe", t0);
			if (tool != BlockType.Air)
			{
				_tableCraftingResultBlock = new ItemStack(tool, 1);
				return;
			}
		}

		// Shovel: 1 Material, 2 Stick
		if (nonAirCount == 3)
		{
			BlockType mat = BlockType.Air;
			bool matchShovel = false;

			// Col 0
			if (t0 != BlockType.Air && t3 == BlockType.Stick && t6 == BlockType.Stick && t1 == BlockType.Air && t2 == BlockType.Air && t4 == BlockType.Air && t5 == BlockType.Air && t7 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t0;
				matchShovel = true;
			}
			// Col 1
			else if (t1 != BlockType.Air && t4 == BlockType.Stick && t7 == BlockType.Stick && t0 == BlockType.Air && t2 == BlockType.Air && t3 == BlockType.Air && t5 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t1;
				matchShovel = true;
			}
			// Col 2
			else if (t2 != BlockType.Air && t5 == BlockType.Stick && t8 == BlockType.Stick && t0 == BlockType.Air && t1 == BlockType.Air && t3 == BlockType.Air && t4 == BlockType.Air && t6 == BlockType.Air && t7 == BlockType.Air)
			{
				mat = t2;
				matchShovel = true;
			}

			if (matchShovel)
			{
				BlockType tool = GetToolForMaterial("shovel", mat);
				if (tool != BlockType.Air)
				{
					_tableCraftingResultBlock = new ItemStack(tool, 1);
					return;
				}
			}
		}

		// Axe: 3 Material, 2 Stick
		if (nonAirCount == 5)
		{
			BlockType mat = BlockType.Air;
			bool matchAxe = false;

			// Right-facing Col 1 handle: Materials 0,1,3. Sticks 4,7
			if (t0 != BlockType.Air && t0 == t1 && t1 == t3 && t4 == BlockType.Stick && t7 == BlockType.Stick &&
				t2 == BlockType.Air && t5 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t0;
				matchAxe = true;
			}
			// Left-facing Col 1 handle: Materials 1,2,5. Sticks 4,7
			else if (t1 != BlockType.Air && t1 == t2 && t2 == t5 && t4 == BlockType.Stick && t7 == BlockType.Stick &&
				t0 == BlockType.Air && t3 == BlockType.Air && t6 == BlockType.Air && t8 == BlockType.Air)
			{
				mat = t1;
				matchAxe = true;
			}

			if (matchAxe)
			{
				BlockType tool = GetToolForMaterial("axe", mat);
				if (tool != BlockType.Air)
				{
					_tableCraftingResultBlock = new ItemStack(tool, 1);
					return;
				}
			}
		}

		_tableCraftingResultBlock = ItemStack.Empty;
	}

	private void RefreshInventoryUI()
	{
		foreach (var slot in _inventorySlotsList)
		{
			if (slot.IsCrafting || slot.IsCraftingResult)
			{
				slot.GuiControl.Visible = !_isCraftingTableOpen && !_isFurnaceOpen;
			}
			else if (slot.IsTableCrafting || slot.IsTableCraftingResult)
			{
				slot.GuiControl.Visible = _isCraftingTableOpen;
			}
			else if (slot.IsFurnaceInput || slot.IsFurnaceFuel || slot.IsFurnaceOutput)
			{
				slot.GuiControl.Visible = _isFurnaceOpen;
			}

			if (slot.GuiControl.Visible)
			{
				ItemStack item = GetSlotItem(slot);

				if (item.IsEmpty)
				{
					slot.IconRect.Visible = false;
					slot.CountLabel.Text = "";
				}
				else
				{
					slot.IconRect.Visible = true;
					SetTextureForItem(slot.IconRect, item.Type);
					slot.CountLabel.Text = item.Count > 1 ? item.Count.ToString() : "";
				}
			}
		}

		var cursorCountLabel = _cursorIcon.GetNodeOrNull<Label>("CountLabel");
		if (cursorCountLabel == null)
		{
			cursorCountLabel = new Label();
			cursorCountLabel.Name = "CountLabel";
			cursorCountLabel.AddThemeFontSizeOverride("font_size", 12);
			cursorCountLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			cursorCountLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
			cursorCountLabel.AddThemeConstantOverride("shadow_offset_x", 1);
			cursorCountLabel.AddThemeConstantOverride("shadow_offset_y", 1);
			cursorCountLabel.AnchorLeft = 1.0f;
			cursorCountLabel.AnchorTop = 1.0f;
			cursorCountLabel.AnchorRight = 1.0f;
			cursorCountLabel.AnchorBottom = 1.0f;
			cursorCountLabel.OffsetLeft = -25;
			cursorCountLabel.OffsetTop = -18;
			cursorCountLabel.OffsetRight = -2;
			cursorCountLabel.OffsetBottom = -2;
			cursorCountLabel.HorizontalAlignment = HorizontalAlignment.Right;
			cursorCountLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_cursorIcon.AddChild(cursorCountLabel);
		}

		if (_cursorItem.IsEmpty)
		{
			_cursorIcon.Visible = false;
			cursorCountLabel.Text = "";
		}
		else
		{
			_cursorIcon.Visible = true;
			SetTextureForItem(_cursorIcon, _cursorItem.Type);
			cursorCountLabel.Text = _cursorItem.Count > 1 ? _cursorItem.Count.ToString() : "";
		}
	}

	private void UpdateHotbarIcons()
	{
		for (int i = 0; i < _hotbarBlocks.Length; i++)
		{
			var slotFrame = _hotbarSlots[i];
			var blockPreview = slotFrame.GetChild<TextureRect>(0);
			var countLabel = slotFrame.GetNode<Label>("CountLabel");
			
			if (_hotbarBlocks[i].IsEmpty)
			{
				blockPreview.Visible = false;
				countLabel.Text = "";
			}
			else
			{
				blockPreview.Visible = true;
				SetTextureForItem(blockPreview, _hotbarBlocks[i].Type);
				countLabel.Text = _hotbarBlocks[i].Count > 1 ? _hotbarBlocks[i].Count.ToString() : "";
			}
		}
	}

	public bool AddBlockToInventory(BlockType type, int count = 1)
	{
		var tempStack = new ItemStack(type, count);
		bool success = AddItemStackToInventory(ref tempStack, hotbarOnly: false, mainInventoryOnly: false);
		if (success)
		{
			UpdateHotbarIcons();
			RefreshInventoryUI();
		}
		return success && tempStack.IsEmpty;
	}

	public static bool IsFood(BlockType type)
	{
		return type == BlockType.Apple || type == BlockType.RawPorkchop || type == BlockType.CookedPorkchop;
	}

	public static bool IsPlaceable(BlockType type)
	{
		return type == BlockType.Grass ||
			   type == BlockType.Dirt ||
			   type == BlockType.Stone ||
			   type == BlockType.Wood ||
			   type == BlockType.Leaves ||
			   type == BlockType.Brick ||
			   type == BlockType.Planks ||
			   type == BlockType.Glass ||
			   type == BlockType.CoalOre ||
			   type == BlockType.IronOre ||
			   type == BlockType.GoldOre ||
			   type == BlockType.DiamondOre ||
			   type == BlockType.Cobblestone ||
			   type == BlockType.CraftingTable ||
			   type == BlockType.Furnace;
	}

	private void EatFood(BlockType foodType)
	{
		int hungerRestore = foodType switch
		{
			BlockType.RawPorkchop => 3,
			BlockType.CookedPorkchop => 8,
			BlockType.Apple => 4,
			_ => 0
		};

		if (hungerRestore > 0)
		{
			_hunger = Mathf.Min(_hunger + hungerRestore, MaxHunger);
			PlayPickupSound();

			var stack = _hotbarBlocks[_selectedHotbarIndex];
			stack.Count -= 1;
			if (stack.Count <= 0)
			{
				_hotbarBlocks[_selectedHotbarIndex] = ItemStack.Empty;
			}
			else
			{
				_hotbarBlocks[_selectedHotbarIndex] = stack;
			}
			UpdateHotbarIcons();
			UpdateStatusBars();
		}
	}

	private void TakeDamage(float amount)
	{
		_health = Mathf.Max(_health - amount, 0f);
		UpdateStatusBars();
		
		if (_health <= 0f)
		{
			Die();
		}
	}

	private void Die()
	{
		_health = MaxHealth;
		_hunger = MaxHunger;
		
		Position = new Vector3(0, 50, 0); // Safe high position above ground
		Velocity = Vector3.Zero;
		
		GD.Print("A játékos meghalt és újjászületett!");
		UpdateStatusBars();
	}

	public void AddXp(float amount)
	{
		_xpProgress += amount;
		while (_xpProgress >= 1f)
		{
			_xpProgress -= 1f;
			_xpLevel++;
		}
		UpdateStatusBars();
	}

	private void CreateStatusBarsUI()
	{
		var hudNode = GetNode<Control>("UI/HUD");

		if (hudNode.HasNode("XpBarBg")) hudNode.GetNode("XpBarBg").QueueFree();
		if (hudNode.HasNode("HealthBarBg")) hudNode.GetNode("HealthBarBg").QueueFree();
		if (hudNode.HasNode("HungerBarBg")) hudNode.GetNode("HungerBarBg").QueueFree();

		// 1. XP Bar (kék)
		var xpBarBg = new ColorRect();
		xpBarBg.Name = "XpBarBg";
		xpBarBg.Color = new Color(0.05f, 0.05f, 0.05f, 0.6f);
		xpBarBg.AnchorLeft = 0.5f;
		xpBarBg.AnchorRight = 0.5f;
		xpBarBg.AnchorTop = 1.0f;
		xpBarBg.AnchorBottom = 1.0f;
		xpBarBg.OffsetLeft = -286;
		xpBarBg.OffsetRight = 286;
		xpBarBg.OffsetTop = -88;
		xpBarBg.OffsetBottom = -82;
		hudNode.AddChild(xpBarBg);

		_xpBarFill = new ColorRect();
		_xpBarFill.Name = "XpBarFill";
		_xpBarFill.Color = new Color(0.1f, 0.6f, 0.9f, 0.9f); // Cyan/blue
		xpBarBg.AddChild(_xpBarFill);

		// 2. Health Bar (piros)
		var healthBarBg = new ColorRect();
		healthBarBg.Name = "HealthBarBg";
		healthBarBg.Color = new Color(0.05f, 0.05f, 0.05f, 0.6f);
		healthBarBg.AnchorLeft = 0.5f;
		healthBarBg.AnchorRight = 0.5f;
		healthBarBg.AnchorTop = 1.0f;
		healthBarBg.AnchorBottom = 1.0f;
		healthBarBg.OffsetLeft = -286;
		healthBarBg.OffsetRight = 286;
		healthBarBg.OffsetTop = -102;
		healthBarBg.OffsetBottom = -94;
		hudNode.AddChild(healthBarBg);

		_healthBarFill = new ColorRect();
		_healthBarFill.Name = "HealthBarFill";
		_healthBarFill.Color = new Color(0.9f, 0.15f, 0.15f, 0.9f); // Red
		healthBarBg.AddChild(_healthBarFill);

		// 3. Hunger Bar (barna)
		var hungerBarBg = new ColorRect();
		hungerBarBg.Name = "HungerBarBg";
		hungerBarBg.Color = new Color(0.05f, 0.05f, 0.05f, 0.6f);
		hungerBarBg.AnchorLeft = 0.5f;
		hungerBarBg.AnchorRight = 0.5f;
		hungerBarBg.AnchorTop = 1.0f;
		hungerBarBg.AnchorBottom = 1.0f;
		hungerBarBg.OffsetLeft = -286;
		hungerBarBg.OffsetRight = 286;
		hungerBarBg.OffsetTop = -116;
		hungerBarBg.OffsetBottom = -108;
		hudNode.AddChild(hungerBarBg);

		_hungerBarFill = new ColorRect();
		_hungerBarFill.Name = "HungerBarFill";
		_hungerBarFill.Color = new Color(0.6f, 0.4f, 0.25f, 0.9f); // Brown
		hungerBarBg.AddChild(_hungerBarFill);

		UpdateStatusBars();
	}

	private void UpdateStatusBars()
	{
		if (_healthBarFill == null || _hungerBarFill == null || _xpBarFill == null) return;

		const float FullWidth = 572f;

		// Health
		float healthPct = Mathf.Clamp(_health / MaxHealth, 0f, 1f);
		_healthBarFill.Size = new Vector2(FullWidth * healthPct, 8f);

		// Hunger
		float hungerPct = Mathf.Clamp(_hunger / MaxHunger, 0f, 1f);
		_hungerBarFill.Size = new Vector2(FullWidth * hungerPct, 8f);

		// XP
		float xpPct = Mathf.Clamp(_xpProgress, 0f, 1f);
		_xpBarFill.Size = new Vector2(FullWidth * xpPct, 6f);
	}

	private void HandleFallDamage(float yPosition)
	{
		if (!IsOnFloor())
		{
			if (!_wasInAir)
			{
				_wasInAir = true;
				_highestYInAir = yPosition;
			}
			else if (yPosition > _highestYInAir)
			{
				_highestYInAir = yPosition;
			}
		}
		else
		{
			if (_wasInAir)
			{
				float fallDistance = _highestYInAir - yPosition;
				if (fallDistance >= 4.0f)
				{
					float damage = fallDistance - 3.0f;
					TakeDamage(damage);
				}
				_wasInAir = false;
				_highestYInAir = float.MinValue;
			}
		}
	}

	private void HandleHungerAndStarvation(float delta)
	{
		_hungerTimer += delta;
		if (_hungerTimer >= 8f) // Every 8 seconds
		{
			_hungerTimer = 0f;
			if (_hunger > 0f)
			{
				_hunger = Mathf.Max(_hunger - 0.5f, 0f);
				UpdateStatusBars();
			}
		}

		if (_hunger <= 0f)
		{
			_starvationTimer += delta;
			if (_starvationTimer >= 4f) // Every 4 seconds
			{
				_starvationTimer = 0f;
				TakeDamage(1.0f);
			}
		}
		else
		{
			_starvationTimer = 0f;
		}
	}

	private void HandleHealthRegen(float delta)
	{
		if (_hunger >= 18f && _health < MaxHealth)
		{
			_regenTimer += delta;
			if (_regenTimer >= 4f) // Regenerate 1 health every 4 seconds
			{
				_regenTimer = 0f;
				_health = Mathf.Min(_health + 1.0f, MaxHealth);
				UpdateStatusBars();
			}
		}
		else
		{
			_regenTimer = 0f;
		}
	}

	private class InventorySlot
	{
		public int Index { get; set; }
		public bool IsHotbar { get; set; }
		public bool IsArmor { get; set; }
		public bool IsCrafting { get; set; }
		public bool IsCraftingResult { get; set; }
		public bool IsTableCrafting { get; set; }
		public bool IsTableCraftingResult { get; set; }
		public bool IsFurnaceInput { get; set; }
		public bool IsFurnaceFuel { get; set; }
		public bool IsFurnaceOutput { get; set; }
		public Control GuiControl { get; set; } = null!;
		public TextureRect IconRect { get; set; } = null!;
		public Label CountLabel { get; set; } = null!;
	}
}
