using Godot;
using System;

namespace Minecraft;

public partial class Pig : PassiveMob
{
	public override void _Ready()
	{
		MaxHealth = 10f;
		WanderSpeed = 1.0f;
		PanicSpeed = 3.0f;
		base._Ready();
	}

	protected override void SpawnDrops()
	{
		if (_world == null) return;

		int count = Random.Shared.Next(1, 4); // 1 to 3 porkchops
		for (int i = 0; i < count; i++)
		{
			// Spawn with a slight random offset to prevent them sticking together
			Vector3 offset = new Vector3(
				(float)(Random.Shared.NextDouble() * 0.4 - 0.2),
				0.2f,
				(float)(Random.Shared.NextDouble() * 0.4 - 0.2)
			);
			_world.SpawnBlockItem(GlobalPosition + offset, BlockType.RawPorkchop);
		}
		GD.Print($"Pig died, dropped {count} Raw Porkchops.");
	}
}
