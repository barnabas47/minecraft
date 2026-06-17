using Godot;
using System;

namespace Minecraft;

public partial class BlockItem : CharacterBody3D
{
	public BlockType Type { get; set; }
	
	private MeshInstance3D _meshInstance = null!;
	private float _age = 0f;
	private Vector3 _rotSpeed;

	public override void _Ready()
	{
		// Ütközési maszkok beállítása: ne ütközzön a játékossal közvetlenül, csak a világgal
		CollisionLayer = 0; 
		CollisionMask = 1;  // Statikus világ rétege

		// 3D háló létrehozása a típushoz tartozó textúrával
		_meshInstance = new MeshInstance3D();
		_meshInstance.Mesh = Chunk.CreateBlockMesh(Type);
		AddChild(_meshInstance);

		// Hozzáadunk egy ütközési alakot, hogy a tárgy ne essen át a földön
		var colShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = new Vector3(0.3f, 0.3f, 0.3f);
		colShape.Shape = boxShape;
		AddChild(colShape);

		// Kezdő dobási irány és sebesség (kicsit fel és véletlenszerű oldalra)
		Velocity = new Vector3(
			(float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5f,
			3.0f,
			(float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5f
		);

		// Véletlenszerű forgási sebesség
		_rotSpeed = new Vector3(
			(float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 3f,
			(float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 3f,
			(float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 3f
		);
	}

	public override void _PhysicsProcess(double delta)
	{
		_age += (float)delta;

		Vector3 vel = Velocity;
		if (!IsOnFloor())
		{
			// Gravitáció
			vel.Y -= 9.8f * (float)delta;
		}
		else
		{
			// Súrlódás a talajon
			vel.X = Mathf.MoveToward(vel.X, 0, 2f * (float)delta);
			vel.Z = Mathf.MoveToward(vel.Z, 0, 2f * (float)delta);
			vel.Y = 0f;
		}

		Velocity = vel;
		MoveAndSlide();

		// Forgatás animáció
		_meshInstance.RotateX(_rotSpeed.X * (float)delta);
		_meshInstance.RotateY(_rotSpeed.Y * (float)delta);
		_meshInstance.RotateZ(_rotSpeed.Z * (float)delta);

		// Finom lebegés (bobbing) effekt, ha a földön van
		if (IsOnFloor())
		{
			Vector3 localPos = _meshInstance.Position;
			localPos.Y = Mathf.Sin(_age * 3f) * 0.05f;
			_meshInstance.Position = localPos;
		}

		// Játékos megkeresése és mágneses szívás
		var player = GetParent().GetNodeOrNull<Player>("Player");
		if (player != null)
		{
			float dist = Position.DistanceTo(player.Position);
			if (dist < 2.5f && _age > 0.5f) // Csak fél másodperc után gyűjthető be, hogy ne vegyük fel azonnal
			{
				Vector3 dir = (player.Position - Position).Normalized();
				Position += dir * 5.0f * (float)delta;

				if (dist < 0.8f)
				{
					// Begyűjtés próbája az inventory-ba
					if (player.AddBlockToInventory(Type))
					{
						player.PlayPickupSound();
						QueueFree();
					}
				}
			}
		}

		// Despawn 60 másodperc után
		if (_age > 60f)
		{
			QueueFree();
		}
	}
}
