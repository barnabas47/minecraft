using Godot;
using System;

namespace Minecraft;

public abstract partial class PassiveMob : Mob
{
	[Export] public float WanderSpeed { get; set; } = 1.0f;
	[Export] public float PanicSpeed { get; set; } = 3.0f;

	protected enum MobState { Idle, Wander, Panic }
	protected MobState CurrentState = MobState.Idle;

	private float _stateTimer = 0f;
	private Vector3 _wanderDirection = Vector3.Zero;
	private float _panicTimer = 0f;
	private Vector3 _panicDirection = Vector3.Zero;

	public override void _Ready()
	{
		base._Ready();
		MoveSpeed = WanderSpeed;
		ChooseNextState();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (CurrentState == MobState.Panic)
		{
			_panicTimer -= (float)delta;
			if (_panicTimer <= 0f)
			{
				CurrentState = MobState.Idle;
				MoveSpeed = WanderSpeed;
				ChooseNextState();
			}
			else
			{
				// Run in panic direction
				TargetVelocity = _panicDirection * PanicSpeed;
			}
		}
		else
		{
			_stateTimer -= (float)delta;
			if (_stateTimer <= 0f)
			{
				ChooseNextState();
			}

			if (CurrentState == MobState.Wander)
			{
				TargetVelocity = _wanderDirection * WanderSpeed;
			}
			else
			{
				TargetVelocity = Vector3.Zero;
			}
		}

		base._PhysicsProcess(delta);
	}

	private void ChooseNextState()
	{
		// 50% chance to idle, 50% chance to wander
		if (Random.Shared.Next(0, 2) == 0)
		{
			CurrentState = MobState.Idle;
			_stateTimer = (float)(Random.Shared.NextDouble() * 3.0 + 2.0); // 2 to 5 seconds
		}
		else
		{
			CurrentState = MobState.Wander;
			_stateTimer = (float)(Random.Shared.NextDouble() * 2.0 + 1.0); // 1 to 3 seconds
			
			// Pick a random horizontal direction
			float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
			_wanderDirection = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)).Normalized();
		}
	}

	public override void TakeDamage(float amount, Vector3 knockbackSource)
	{
		base.TakeDamage(amount, knockbackSource);
		
		// Panic when hit!
		CurrentState = MobState.Panic;
		_panicTimer = 4.0f; // Panic for 4 seconds
		
		// Flee away from knockback source
		Vector3 fleeDir = (GlobalPosition - knockbackSource);
		fleeDir.Y = 0f;
		if (fleeDir.LengthSquared() > 0.001f)
		{
			_panicDirection = fleeDir.Normalized();
		}
		else
		{
			// Fallback to random direction
			float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
			_panicDirection = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)).Normalized();
		}
	}
}
