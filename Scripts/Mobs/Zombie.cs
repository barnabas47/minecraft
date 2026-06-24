using Godot;
using System;

namespace Minecraft;

public partial class Zombie : HostileMob
{
	public override void _Ready()
	{
		MaxHealth = 20f;
		MoveSpeed = 1.6f;
		AttackDamage = 3.0f;
		DetectionRange = 16.0f;
		AttackCooldown = 1.5f;
		AttackRange = 1.5f;
		base._Ready();

		Color green = Color.FromHtml("#4b6f44");
		Color blue = Color.FromHtml("#00a2e8");
		Color purple = Color.FromHtml("#3f48cc");

		SetupDirectMesh("Body", new Vector3(0f, 1.2f, 0f), new Vector3(0.6f, 0.8f, 0.3f), blue);
		SetupDirectMesh("Head", new Vector3(0f, 1.85f, 0f), new Vector3(0.5f, 0.5f, 0.5f), green);

		SetupDefaultMesh("LegLeft", new Vector3(-0.15f, 0.8f, 0f), new Vector3(0.2f, 0.8f, 0.2f), new Vector3(0f, -0.4f, 0f), purple);
		SetupDefaultMesh("LegRight", new Vector3(0.15f, 0.8f, 0f), new Vector3(0.2f, 0.8f, 0.2f), new Vector3(0f, -0.4f, 0f), purple);
		SetupDefaultMesh("ArmLeft", new Vector3(-0.4f, 1.5f, 0f), new Vector3(0.2f, 0.2f, 0.8f), new Vector3(0f, 0f, -0.4f), green);
		SetupDefaultMesh("ArmRight", new Vector3(0.4f, 1.5f, 0f), new Vector3(0.2f, 0.2f, 0.8f), new Vector3(0f, 0f, -0.4f), green);

		// Configure collision shape to prevent floating: make the bottom of the shape y = 0
		var colShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (colShape != null)
		{
			var box = new BoxShape3D();
			box.Size = new Vector3(0.6f, 1.8f, 0.6f);
			colShape.Shape = box;
			colShape.Position = new Vector3(0f, 0.9f, 0f);
		}
	}

	protected override void SpawnDrops()
	{
		if (_world == null) return;

		int count = Random.Shared.Next(1, 3); // 1 to 2 coal ores
		for (int i = 0; i < count; i++)
		{
			Vector3 offset = new Vector3(
				(float)(Random.Shared.NextDouble() * 0.4 - 0.2),
				0.2f,
				(float)(Random.Shared.NextDouble() * 0.4 - 0.2)
			);
			_world.SpawnBlockItem(GlobalPosition + offset, BlockType.CoalOre);
		}
		GD.Print($"Zombie died, dropped {count} Coal Ores.");
	}
}
