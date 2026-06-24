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
