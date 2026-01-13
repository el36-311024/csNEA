using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public partial class team : RigidBody3D
{
	private int MaxHealth = 100;
	private int CurrentHealth;
	private Marker3D spawnMarker;
	private string gunName;
	
	private NavigationAgent3D MovementTeam;
	private float moveSpeed = 5f;
	private Area3D TeamDetection;
	private Node3D currentTarget;
	private float detectionRange = 1f;
	private float rotationSpeed = 6f;
	
	private int ammoAmount;
	public int currentAmmo;
	private float reloadTime;
	private float fireRate;
	private bool isReloading = false;
	private bool isShooting = false;
	private Node3D gunHolder;
	private PackedScene bulletTeamScene;
	
	private float ragdollTime = 3.0f;
	private bool isDead = false;
	public event Action<Marker3D, string> TeamDied;
	
	public override void _Ready()
	{
		CurrentHealth = MaxHealth;
		
		AxisLockAngularX = true;
   		AxisLockAngularZ = true;
		
		MovementTeam = GetNode<NavigationAgent3D>("MovementTeam");
		TeamDetection = GetNode<Area3D>("TeamDetection");
		gunHolder = GetNode<Node3D>("ArmPivot/ArmMovement/GunPivot/GunHolder");
		bulletTeamScene = GD.Load<PackedScene>("res://BulletTeam.tscn");
		AddToGroup("Team");
	}

	private void SetGunStats(string gun)
	{
		switch (gun)
		{
			case "Pistol":
				detectionRange = 25f;
				ammoAmount = 12;
				reloadTime = 2f;
				fireRate = 0.3f;
				break;
			case "Rifle1":
				detectionRange = 35f;
				ammoAmount = 50;
				reloadTime = 3f;
				fireRate = 0.5f;
				break;
			case "Rifle2":
				detectionRange = 30f;
				ammoAmount = 36;
				reloadTime = 2f;
				fireRate = 0.3f;
				break;
			case "Heavy":
				detectionRange = 25f;
				ammoAmount = 100;
				reloadTime = 4f;
				fireRate = 0.2f;
				break;
			case "Sniper":
				detectionRange = 60f;
				ammoAmount = 2;
				reloadTime = 4f;
				fireRate = 2.5f;
				break;
			default:
				detectionRange = 15f;
				ammoAmount = 10;
				reloadTime = 2f;
				fireRate = 0.5f;
				break;
		}
		currentAmmo = ammoAmount;
	}
	
	private void UpdateDetectionRange()
	{
		var shapeNode = TeamDetection.GetNode<CollisionShape3D>("CollisionShape3D");

		if (shapeNode.Shape is SphereShape3D sphere)
		{
			SphereShape3D newSphere = new SphereShape3D();
			newSphere.Radius = detectionRange;

			shapeNode.Shape = newSphere;
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		UpdateTarget();
		LookAtTargetOrDirection(delta);
		Shooting();
	}
	
	private void UpdateTarget()
	{
		var bodies = TeamDetection.GetOverlappingBodies();
		if (bodies.Count == 0)
		{
			currentTarget = null;
			return;
		}

		Node3D nearest = null;
		float nearestDist = float.MaxValue;

		foreach (var body in bodies)
		{
			if (body is Node3D node && (node.IsInGroup("Enemy")))
			{
				float dist = GlobalPosition.DistanceTo(node.GlobalPosition);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					nearest = node;
				}
			}
		}
		currentTarget = nearest;
	}
	
	private void LookAtTargetOrDirection(double delta)
	{
		if (currentTarget == null || !IsInstanceValid(currentTarget)) 
		return;

		Node3D armPivot = GetNodeOrNull<Node3D>("ArmPivot");
		Vector3 targetCenter = currentTarget.GlobalPosition;

		Vector3 toTarget = targetCenter - armPivot.GlobalPosition;
		if (toTarget.LengthSquared() < 0.001f)
			return;

		Vector3 flatDir = toTarget;
		flatDir.Y = 0;
		if (flatDir.LengthSquared() > 0.001f)
		{
			flatDir = flatDir.Normalized();

			Vector3 currentDir = -GlobalTransform.Basis.Z;
			currentDir.Y = 0;
			currentDir = currentDir.Normalized();

			float angle = Mathf.Atan2(flatDir.X, flatDir.Z) - Mathf.Atan2(currentDir.X, currentDir.Z);
			angle = Mathf.Wrap(angle, -Mathf.Pi, Mathf.Pi);

			float rotateStep = rotationSpeed * (float)delta;
			angle = Mathf.Clamp(angle, -rotateStep, rotateStep);

			RotateY(angle);
		}

		float targetDistance = new Vector2(toTarget.X, toTarget.Z).Length();
		float pitch = Mathf.Atan2(toTarget.Y, targetDistance);

		pitch = Mathf.Clamp(pitch, -Mathf.DegToRad(45f), Mathf.DegToRad(45f));

		Vector3 armRot = armPivot.Rotation;
		armRot.X = Mathf.Lerp(armRot.X, pitch, 6f * (float)delta);
		armPivot.Rotation = armRot;

		armPivot.LookAt(targetCenter, Vector3.Up);
	}
	
	private async void Shooting()
	{
		if (currentTarget == null || isReloading || isShooting) return;

		float distance = GlobalPosition.DistanceTo(currentTarget.GlobalPosition);
		if (distance <= detectionRange)
		{
			if (currentAmmo > 0)
			{
				isShooting = true;
				await Shoot();
				isShooting = false;
			}
			else
			{
				await Reload();
			}
		}
	}

	private async Task Shoot()
	{
		currentAmmo--;
		Node3D bulletTeamSpawn = gunHolder.GetNodeOrNull<Node3D>($"{gunName}/BulletHole");
		BulletTeam bulletTeamInstance = bulletTeamScene.Instantiate<BulletTeam>();
		bulletTeamInstance.GlobalTransform = bulletTeamSpawn.GlobalTransform;
		bulletTeamInstance.SetGunType(gunName);
		bulletTeamInstance.TeamShooter = this;
		bulletTeamInstance.Direction = -bulletTeamSpawn.GlobalTransform.Basis.Z;
		GetTree().CurrentScene.AddChild(bulletTeamInstance);

		await ToSignal(GetTree().CreateTimer(fireRate), "timeout");
	}

	private async Task Reload()
	{
		isReloading = true;
		await RotateGunWhileReloading(reloadTime);
		currentAmmo = ammoAmount;
		isReloading = false;
	}

	private async Task RotateGunWhileReloading(float duration)
	{
		float elapsed = 0f;
		Node3D gunPivot = GetNodeOrNull<Node3D>("ArmPivot/ArmMovement/GunPivot");
		if (gunPivot == null)
		{
			return;
		}

		float totalRotation = Mathf.Tau;
		float rotationSpeed = totalRotation / duration;

		while (elapsed < duration && IsInsideTree())
		{
			if (!IsInstanceValid(gunPivot))
			{
				break;
			}

			float delta = (float)GetProcessDeltaTime();
			elapsed += delta;

			gunPivot.RotateObjectLocal(Vector3.Forward, rotationSpeed * delta);

			if (IsInsideTree())
			{
				await ToSignal(GetTree(), "process_frame");
			}
			else
			{
				break;
			}
		}

		if (IsInstanceValid(gunPivot))
		{
			gunPivot.Rotation = Vector3.Zero;
		}
	}

	public void Initialize(Marker3D marker, string gun)
	{
		spawnMarker = marker;
		gunName = gun;
		
		SetGunStats(gunName);
		CallDeferred(nameof(UpdateDetectionRange));
		EquipGun(gunName);
	}
	
	private void EquipGun(string gun)
	{
		string gunPath = $"res://{gun}.tscn";
		PackedScene gunScene = GD.Load<PackedScene>(gunPath);

		Node3D gunHolder;
		gunHolder = GetNode<Node3D>("ArmPivot/ArmMovement/GunPivot/GunHolder");

		foreach (Node child in gunHolder.GetChildren())
		{
			gunHolder.RemoveChild(child);
			child.QueueFree();
		}

		Node3D gunInstance = gunScene.Instantiate<Node3D>();
		gunHolder.AddChild(gunInstance);
	}

	public void TakeDamage(int amount)
	{
		if (isDead)
		{ 
			return;
		}

		CurrentHealth -= amount;
		if (CurrentHealth <= 0)
		{
			isDead = true;
			KillManager.Instance.AddEnemyKill();
			Die();
		}
	}

	private async void Die()
	{
		SetProcess(false);
		SetPhysicsProcess(false);
		isShooting = false;
		isReloading = false;
		
		DropGun();

		Freeze = false;
		GravityScale = 1.5f;
		AxisLockAngularX = false;
		AxisLockAngularY = false;
		AxisLockAngularZ = false;
		Vector3 impulseDir = -GlobalTransform.Basis.Z + Vector3.Up * 0.6f;
		ApplyImpulse(impulseDir.Normalized() * 6f);
		await ToSignal(GetTree().CreateTimer(ragdollTime), "timeout");
		TeamDied?.Invoke(spawnMarker, gunName);
		CallDeferred(nameof(DeferredDie));
	}
	
	private void DeferredDie()
	{
		QueueFree();
	}
	
	private void DropGun()
	{
		if (!IsInsideTree())
			return;

		if (!IsInstanceValid(gunHolder))
			return;

		if (gunHolder.GetChildCount() == 0)
			return;

		Node3D gunVisual = gunHolder.GetChild<Node3D>(0);
		gunHolder.RemoveChild(gunVisual);

		RigidBody3D rb = new RigidBody3D
		{
			Name = "DroppedGun",
			GravityScale = 1f,
			Freeze = false,
			Sleeping = false
		};

		CollisionShape3D col = new CollisionShape3D();
		col.Shape = new BoxShape3D
		{
			Size = new Vector3(0.4f, 0.2f, 1.0f)
		};
		rb.AddChild(col);
		rb.AddChild(gunVisual);
		GetParent().AddChild(rb);
		rb.GlobalTransform = gunHolder.GlobalTransform;
		Vector3 throwDir = (-GlobalTransform.Basis.Z + Vector3.Up * 0.5f).Normalized();
		rb.ApplyImpulse(throwDir * 3.5f);
		StartGunDespawn(rb);
	}
	
	private async void DespawnGun(RigidBody3D rb)
	{
		await ToSignal(GetTree().CreateTimer(5f), "timeout");

		if (IsInstanceValid(rb))
			rb.QueueFree();
	}
	
	private void StartGunDespawn(RigidBody3D rb)
	{
		Timer t = new Timer();
		t.WaitTime = 5f;
		t.OneShot = true;
		rb.AddChild(t);

		t.Timeout += () =>
		{
			if (IsInstanceValid(rb))
				rb.QueueFree();
		};

		t.Start();
	}
}
