using Godot;
using System;

namespace Minecraft;

public abstract partial class Mob : CharacterBody3D
{
	[Export] public float MaxHealth { get; set; } = 10f;
	[Export] public float Health { get; set; }
	[Export] public float MoveSpeed { get; set; } = 2.0f;
	[Export] public float Gravity { get; set; } = 15.0f; // Matching Player's gravity

	protected Node3D? ModelNode;
	protected Vector3 TargetVelocity = Vector3.Zero;
	protected float Age = 0f;
	protected World _world = null!;

	public override void _Ready()
	{
		Health = MaxHealth;
		ModelNode = GetNodeOrNull<Node3D>("Model");
		CollisionLayer = 4; // Mobs layer
		CollisionMask = 1 | 4; // Collide with static world (1) and other mobs (4)

		var worldNode = GetParent();
		while (worldNode != null && worldNode is not World)
		{
			worldNode = worldNode.GetParent();
		}
		_world = (World)worldNode!;
	}

	public override void _PhysicsProcess(double delta)
	{
		Age += (float)delta;
		Vector3 velocity = Velocity;

		// Apply gravity
		if (!IsOnFloor())
		{
			velocity.Y -= Gravity * (float)delta;
		}
		else
		{
			velocity.Y = 0f;
		}

		// Apply horizontal movement (AI sets TargetVelocity)
		velocity.X = TargetVelocity.X;
		velocity.Z = TargetVelocity.Z;

		// Obstacle jumping: if blocked by a wall while trying to move horizontally, jump!
		if (IsOnFloor() && IsOnWall() && new Vector2(TargetVelocity.X, TargetVelocity.Z).LengthSquared() > 0.01f)
		{
			velocity.Y = 6.0f;
		}

		Velocity = velocity;
		MoveAndSlide();

		// Face movement direction if moving horizontally
		Vector2 horizontalVel = new Vector2(Velocity.X, Velocity.Z);
		if (horizontalVel.LengthSquared() > 0.001f)
		{
			float targetAngle = Mathf.Atan2(Velocity.X, Velocity.Z);
			Rotation = new Vector3(Rotation.X, targetAngle, Rotation.Z);
			AnimateLegs(delta, horizontalVel.Length());
		}
		else
		{
			ResetLegs();
		}
	}

	public virtual void TakeDamage(float amount, Vector3 knockbackSource)
	{
		Health = Mathf.Max(Health - amount, 0f);
		GD.Print($"{Name} took {amount} damage. Current Health: {Health}");

		// Simple knockback away from source
		Vector3 dir = (GlobalPosition - knockbackSource).Normalized();
		dir.Y = 0.25f; // Slight vertical pop
		Velocity = dir * 6.0f;

		if (Health <= 0f)
		{
			Die();
		}
	}

	protected virtual void Die()
	{
		SpawnDrops();
		QueueFree();
	}

	protected abstract void SpawnDrops();

	private void AnimateLegs(double delta, float speed)
	{
		if (ModelNode == null) return;

		// Swing frequency increases with movement speed
		float freq = 12f;
		float amp = 0.6f;
		float swingAngle = Mathf.Sin(Age * freq) * amp;

		// Left / Right alternating legs
		RotateLeg("LegLeft", swingAngle);
		RotateLeg("LegRight", -swingAngle);
		RotateLeg("LegLeftBack", -swingAngle);
		RotateLeg("LegRightBack", swingAngle);
	}

	private void ResetLegs()
	{
		if (ModelNode == null) return;
		RotateLeg("LegLeft", 0f);
		RotateLeg("LegRight", 0f);
		RotateLeg("LegLeftBack", 0f);
		RotateLeg("LegRightBack", 0f);
	}

	private void RotateLeg(string path, float angle)
	{
		var leg = ModelNode?.GetNodeOrNull<Node3D>(path);
		if (leg != null)
		{
			leg.Rotation = new Vector3(angle, leg.Rotation.Y, leg.Rotation.Z);
		}
	}

	protected void SetupDefaultMesh(string nodeName, Vector3 jointPos, Vector3 boxSize, Vector3 meshOffset, Color color)
	{
		var node = ModelNode?.GetNodeOrNull<Node3D>(nodeName);
		if (node == null) return;

		node.Position = jointPos;

		if (node is MeshInstance3D meshInst)
		{
			if (meshInst.Mesh == null && meshInst.GetChildCount() == 0)
			{
				var childMesh = new MeshInstance3D();
				var box = new BoxMesh();
				box.Size = boxSize;
				childMesh.Mesh = box;
				childMesh.Position = meshOffset;

				var mat = new StandardMaterial3D();
				mat.AlbedoColor = color;
				mat.Roughness = 0.8f;
				childMesh.MaterialOverride = mat;

				meshInst.AddChild(childMesh);
			}
		}
	}

	protected void SetupDirectMesh(string nodeName, Vector3 pos, Vector3 boxSize, Color color)
	{
		var node = ModelNode?.GetNodeOrNull<Node3D>(nodeName);
		if (node == null) return;

		node.Position = pos;

		if (node is MeshInstance3D meshInst)
		{
			if (meshInst.Mesh == null)
			{
				var box = new BoxMesh();
				box.Size = boxSize;
				meshInst.Mesh = box;

				var mat = new StandardMaterial3D();
				mat.AlbedoColor = color;
				mat.Roughness = 0.8f;
				meshInst.MaterialOverride = mat;
			}
		}
	}
}
