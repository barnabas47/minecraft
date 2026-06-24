using Godot;
using System;

namespace Minecraft;

public abstract partial class HostileMob : Mob
{
	[Export] public float DetectionRange { get; set; } = 16.0f;
	[Export] public float AttackDamage { get; set; } = 2.0f;
	[Export] public float AttackRange { get; set; } = 1.5f;
	[Export] public float AttackCooldown { get; set; } = 1.5f;

	protected Player? TargetPlayer;
	
	private float _attackCooldownTimer = 0f;
	private float _stateTimer = 0f;
	private Vector3 _wanderDirection = Vector3.Zero;
	private bool _isWandering = false;

	public override void _Ready()
	{
		base._Ready();
		FindPlayer();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_attackCooldownTimer > 0f)
		{
			_attackCooldownTimer -= (float)delta;
		}

		if (TargetPlayer == null)
		{
			FindPlayer();
		}

		if (TargetPlayer != null && TargetPlayer.IsInsideTree())
		{
			float distance = GlobalPosition.DistanceTo(TargetPlayer.GlobalPosition);

			if (distance <= DetectionRange)
			{
				// Chase player
				Vector3 dir = (TargetPlayer.GlobalPosition - GlobalPosition);
				dir.Y = 0f; // Only move horizontally
				if (dir.LengthSquared() > 0.001f)
				{
					TargetVelocity = dir.Normalized() * MoveSpeed;
				}

				// Check attack range
				if (distance <= AttackRange && _attackCooldownTimer <= 0f)
				{
					Attack(TargetPlayer);
				}
			}
			else
			{
				// Wandering/Idle AI when player is out of range
				WanderProcess((float)delta);
			}
		}
		else
		{
			WanderProcess((float)delta);
		}

		base._PhysicsProcess(delta);
	}

	private void FindPlayer()
	{
		var playerNode = GetTree().GetFirstNodeInGroup("Player");
		if (playerNode is Player p)
		{
			TargetPlayer = p;
		}
	}

	private void WanderProcess(float delta)
	{
		_stateTimer -= delta;
		if (_stateTimer <= 0f)
		{
			// Alternate between idle and walking
			_isWandering = !_isWandering;
			_stateTimer = (float)(Random.Shared.NextDouble() * 3.0 + 2.0); // 2 to 5 seconds

			if (_isWandering)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
				_wanderDirection = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)).Normalized();
			}
		}

		if (_isWandering)
		{
			TargetVelocity = _wanderDirection * (MoveSpeed * 0.5f); // Wander at half speed
		}
		else
		{
			TargetVelocity = Vector3.Zero;
		}
	}

	protected virtual void Attack(Player player)
	{
		player.TakeDamage(AttackDamage);
		_attackCooldownTimer = AttackCooldown;
		GD.Print($"{Name} attacked Player for {AttackDamage} damage.");
	}
}
