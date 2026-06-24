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

		Color pink = Color.FromHtml("#ffc0cb");
		Color darkPink = Color.FromHtml("#ffb6c1");

		SetupDirectMesh("Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 0.6f, 1.2f), pink);
		SetupDirectMesh("Head", new Vector3(0f, 0.9f, -0.6f), new Vector3(0.5f, 0.5f, 0.5f), pink);

		SetupDefaultMesh("LegLeft", new Vector3(-0.25f, 0.5f, -0.4f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, -0.25f, 0f), darkPink);
		SetupDefaultMesh("LegRight", new Vector3(0.25f, 0.5f, -0.4f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, -0.25f, 0f), darkPink);
		SetupDefaultMesh("LegLeftBack", new Vector3(-0.25f, 0.5f, 0.4f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, -0.25f, 0f), darkPink);
		SetupDefaultMesh("LegRightBack", new Vector3(0.25f, 0.5f, 0.4f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, -0.25f, 0f), darkPink);
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
