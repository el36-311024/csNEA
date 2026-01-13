using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public partial class enemy : RigidBody3D
{
	private int MaxHealth = 100;
	private int CurrentHealth;
	private Marker3D spawnMarker;
	private string gunName;
	private RayCast3D EnemySight;
	private Vector3 lastKnownTargetPosition;
	private bool forcedHunt = false;
	
	private NavigationAgent3D MovementEnemy;
	private float moveSpeed = 5f;
	private float PreCaptureMoveSpeed = 3.5f;
	private float CapturedMoveSpeed = 3f;
	private float stuckTimer = 0f;
	
	private const float SeparationRadius = 1.0f;
	private const float SeparationStrength = 8f;
	private const float CapturePointSpread = 1.5f;
	
	private EnemyState currentState = EnemyState.GoingToCapture;
	private List<Node3D> capturePoints = new();
	private Node3D currentCapturePoint;
	private RandomNumberGenerator rng = new();

	private Area3D EnemyDetection;
	private Node3D currentTarget;
	private float detectionRange = 1f;
	private float rotationSpeed = 6f;
	
	private Vector3 combatMoveTarget;
	private bool hasCombatMoveTarget = false;
	private float combatMoveRadius = 6f;
	private float combatRepathTime = 0.5f;
	private float combatRepathTimer = 0f;
	private float minCombatDistance = 1f;
	private float baseMinCombatDistance;
	private float strafeAngle = 15f;
	private float strafeSpeed = 1.5f;
	private float huntTimer = 0f;
	private const float MaxHuntTime = 10f;
	private bool isHunting = false;
	private Node3D huntTarget;
	
	private int ammoAmount;
	public int currentAmmo;
	private float reloadTime;
	private float fireRate;
	private bool isReloading = false;
	private bool isShooting = false;
	private Node3D gunHolder;
	private PackedScene bulletEnemyScene;
	
	private float ragdollTime = 3.0f;
	private bool isDead = false;
	public event Action<Marker3D, string> EnemyDied;
	
	private enum EnemyState 
	{
		GoingToCapture,
		InCombat,
		GoingToNearestCapturePoint,
		GoToAnotherPointAfterCapture,
		HuntingTeam,
		HuntingTeamForLeaving,
		WaitingAtCapturePoint,
		GuardCapturePoint,
		CapturePointIsBeingRecaptured,
	}
	
	public override void _Ready()
	{
		CurrentHealth = MaxHealth;
		
		AxisLockAngularX = true;
   		AxisLockAngularZ = true;
		
		MovementEnemy = GetNode<NavigationAgent3D>("MovementEnemy");
		EnemyDetection = GetNode<Area3D>("EnemyDetection");
		gunHolder = GetNode<Node3D>("ArmPivot/ArmMovement/GunPivot/GunHolder");
		bulletEnemyScene = GD.Load<PackedScene>("res://BulletEnemy.tscn");
		EnemySight = GetNode<RayCast3D>("ArmPivot/ArmMovement/GunPivot/EnemySight");
		AddToGroup("Enemy");
		
		EnemyDetection.BodyEntered += OnDetectionBodyEntered;
		capturePoints = GetTree().GetNodesInGroup("CapturePoint").OfType<Node3D>().ToList();
		PickRandomCapturePoint();
	}

	private void SetGunStats(string gun)
	{
		switch (gun)
		{
			case "Pistol":
				detectionRange = 25f;
				minCombatDistance = 10f;
				baseMinCombatDistance = minCombatDistance;
				ammoAmount = 12;
				reloadTime = 2f;
				fireRate = 0.3f;
				break;
			case "Rifle1":
				detectionRange = 35f;
				minCombatDistance = 15f;
				baseMinCombatDistance = minCombatDistance;
				ammoAmount = 50;
				reloadTime = 3f;
				fireRate = 0.5f;
				break;
			case "Rifle2":
				detectionRange = 30f;
				minCombatDistance = 15f;
				baseMinCombatDistance = minCombatDistance;
				ammoAmount = 36;
				reloadTime = 2f;
				fireRate = 0.3f;
				break;
			case "Heavy":
				detectionRange = 25f;
				minCombatDistance = 20f;
				baseMinCombatDistance = minCombatDistance;
				ammoAmount = 100;
				reloadTime = 4f;
				fireRate = 0.2f;
				break;
			case "Sniper":
				detectionRange = 60f;
				minCombatDistance = 45f;
				baseMinCombatDistance = minCombatDistance;
				ammoAmount = 2;
				reloadTime = 4f;
				fireRate = 2.5f;
				break;
			default:
				break;
		}
		currentAmmo = ammoAmount;
	}
	
	private bool UsesRandomRetreat()
	{
		return gunName == "Pistol" || gunName == "Rifle1" || gunName == "Rifle2";
	}
	
	private bool UsesCircleStrafe()
	{
		return gunName == "Heavy" || gunName == "Sniper";
	}
	
	private void UpdateEnemyState()
	{
		bool teamInDetection = HasTeamInDetection();

		if (currentState == EnemyState.HuntingTeam && forcedHunt)
		{
			return;
		}
		
		switch (currentState)
		{
			case EnemyState.GoingToCapture:
				if (teamInDetection)
				{
					currentState = EnemyState.InCombat;
					hasCombatMoveTarget = false;
					break;
				}

				if (HasReachedDestination())
				{
					currentState = EnemyState.WaitingAtCapturePoint;
				}
				break;

			case EnemyState.InCombat:
				if (!teamInDetection && currentTarget != null)
				{
					TriggerHunt(currentTarget.GlobalPosition);
					break;
				}
				break;

			case EnemyState.HuntingTeam:
				huntTimer -= (float)GetPhysicsProcessDeltaTime();

				if (teamInDetection)
				{
					currentState = EnemyState.InCombat;
					break;
				}

				if (huntTimer <= 0f)
				{
					huntTarget = null;
					PickRandomCapturePoint();
					currentState = EnemyState.GoingToCapture;
				}
				break;

			case EnemyState.WaitingAtCapturePoint:
				if (teamInDetection)
				{
					currentState = EnemyState.InCombat;
				}
				break;
		}
	}
	
	private void OnDetectionBodyEntered(Node body)
	{ 
		if (isDead)
			return;
 
		if (body is BulletTeam bullet)
		{ 
			if (bullet.TeamShooter is Node3D teamshooter && teamshooter.IsInGroup("Team"))
			{
				TriggerHunt(teamshooter.GlobalPosition);
			}
		}
	}
	
	private void EnemyMovement(double delta)
	{
		if (moveSpeed <= 0f)
		{
			LinearVelocity = new Vector3(0, LinearVelocity.Y, 0);
			return;
		}

		Vector3 nextPos = MovementEnemy.GetNextPathPosition();
		Vector3 direction = nextPos - GlobalPosition;
		direction.Y = 0;

		if (direction.LengthSquared() < 0.05f)
		{
			MovementEnemy.TargetPosition += GetSeparationOffset();
			return;
		}

		direction = direction.Normalized();

		Vector3 finalDir = direction;

		if (currentState == EnemyState.GoingToCapture)
		{
			Vector3 separation = GetSeparationOffset();
			separation = separation.LimitLength(5f);
			finalDir = (direction + separation).Normalized();
		}

		Vector3 velocity = finalDir * moveSpeed;
		velocity.Y = LinearVelocity.Y;
		LinearVelocity = velocity;
	}
	
	private void UpdateCombatMovement(double delta)
	{
		if (currentTarget == null)
			return;
			
		if (AllyInSight())
		{
			MoveOutOfAllyWay();
			return;
		}
		
		if (IsLowHealth())
		{
			Node3D cover = FindNearestCover();
			if (cover != null)
			{
				MovementEnemy.TargetPosition = cover.GlobalPosition;
				hasCombatMoveTarget = true;
				return; 
			}
		}	

		
		if (IsLowHealth())
			minCombatDistance = baseMinCombatDistance + 10f;
		else
			minCombatDistance = baseMinCombatDistance;

		combatRepathTimer -= (float)delta;
		strafeAngle += strafeSpeed * (float)delta;

		Vector3 awayDir = (GlobalPosition - currentTarget.GlobalPosition);
		awayDir.Y = 0;

		float distance = awayDir.Length();
		if (distance < 0.01f)
			awayDir = Vector3.Forward;

		awayDir = awayDir.Normalized();

		if (distance < minCombatDistance)
		{
			if (UsesRandomRetreat())
			{
				Vector3 randomBack = awayDir * minCombatDistance + new Vector3(rng.RandfRange(-combatMoveRadius, combatMoveRadius), 0, rng.RandfRange(-combatMoveRadius, combatMoveRadius));
				MovementEnemy.TargetPosition = currentTarget.GlobalPosition + randomBack;
			}
			else if (UsesCircleStrafe())
			{
				Vector3 right = awayDir.Cross(Vector3.Up);
				Vector3 circle = right * Mathf.Sin(strafeAngle) * minCombatDistance;
				MovementEnemy.TargetPosition = currentTarget.GlobalPosition + awayDir * minCombatDistance + circle;
				moveSpeed = CapturedMoveSpeed;
			}

			hasCombatMoveTarget = true;
			return;
		}

		if (!hasCombatMoveTarget || combatRepathTimer <= 0f)
		{
			Vector3 offset;

			if (UsesCircleStrafe())
			{
				Vector3 right = awayDir.Cross(Vector3.Up);
				offset = right * Mathf.Sin(strafeAngle) * minCombatDistance;
			}
			else
			{
				offset = new Vector3(rng.RandfRange(-combatMoveRadius, combatMoveRadius), 0, rng.RandfRange(-combatMoveRadius, combatMoveRadius) );
			}

			MovementEnemy.TargetPosition = currentTarget.GlobalPosition + awayDir * minCombatDistance + offset;
			combatRepathTimer = combatRepathTime;
			hasCombatMoveTarget = true;
		}
	}
	
	private void TriggerHunt(Vector3 position)
	{
		lastKnownTargetPosition = position;
		huntTimer = MaxHuntTime;

		forcedHunt = true;
		currentState = EnemyState.HuntingTeam;
	}
	
	private void UpdateHuntMovement()
	{
		MovementEnemy.TargetPosition = lastKnownTargetPosition;

		if (HasReachedDestination())
		{
			huntTimer = 0f;
		}

		Node3D seen = GetRaycastTeam();
		if (seen != null)
		{
			lastKnownTargetPosition = seen.GlobalPosition;
			currentTarget = seen;
		}
	}
	
	private Node3D FindNearestTeam()
	{
		var bodies = EnemyDetection.GetOverlappingBodies();
		Node3D nearest = null;
		float nearestDist = float.MaxValue;

		foreach (var body in bodies)
		{
			if (body is Node3D node && node.IsInGroup("Team"))
			{
				float dist = GlobalPosition.DistanceTo(node.GlobalPosition);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					nearest = node;
				}
			}
		}
		return nearest;
	}
	
	private Vector3 GetSeparationOffset()
	{
		Vector3 separation = Vector3.Zero;

		foreach (Node node in GetTree().GetNodesInGroup("Enemy"))
		{
			if (node == this || node is not Node3D other)
				continue;

			float dist = GlobalPosition.DistanceTo(other.GlobalPosition);
			if (dist > 0 && dist < SeparationRadius)
			{
				Vector3 away = (GlobalPosition - other.GlobalPosition).Normalized();
				separation += away * (SeparationRadius - dist);
			}
		}

		return separation * SeparationStrength;
	}
	
	private bool HasReachedDestination()
	{
		return GlobalPosition.DistanceTo(MovementEnemy.TargetPosition) < 1.5f;
	}

	
	private void UpdateDetectionRange()
	{
		var shapeNode = EnemyDetection.GetNode<CollisionShape3D>("CollisionShape3D");

		if (shapeNode.Shape is SphereShape3D sphere)
		{
			SphereShape3D newSphere = new SphereShape3D();
			newSphere.Radius = detectionRange;

			shapeNode.Shape = newSphere;
		}
	}
	
	private bool HasTeamInDetection()
	{
		var bodies = EnemyDetection.GetOverlappingBodies();
		foreach (var body in bodies)
		{
			if (body is Node3D node && node.IsInGroup("Team"))
				return true;
		}
		return false;
	}
	
	private Node3D GetRaycastTeam()
	{
		Node hit = GetSightHit();
		if (hit is Node3D node && node.IsInGroup("Team"))
			return node;

		return null;
	}
	
	private Node GetSightHit()
	{
		if (!EnemySight.IsColliding())
			return null;

		return EnemySight.GetCollider() as Node;
	}

	private bool AllyInSight()
	{
		Node hit = GetSightHit();
		return hit != null && hit.IsInGroup("Enemy") && hit != this;
	}

	private bool TeamInSight()
	{
		Node hit = GetSightHit();
		return hit != null && hit.IsInGroup("Team");
	}
	
	private void MoveOutOfAllyWay()
	{
		Vector3 right = GlobalTransform.Basis.X;
		Vector3 left = -right;

		Vector3 chosenDir = rng.Randf() > 0.5f ? right : left;
		MovementEnemy.TargetPosition = GlobalPosition + chosenDir * 4f;
		hasCombatMoveTarget = true;
	}
	
	private Node3D FindNearestCover()
	{
		var covers = GetTree().GetNodesInGroup("Cover");
		Node3D nearest = null;
		float nearestDist = float.MaxValue;

		foreach (Node node in covers)
		{
			if (node is not Node3D cover)
				continue;

			float dist = GlobalPosition.DistanceTo(cover.GlobalPosition);
			if (dist < nearestDist)
			{
				nearestDist = dist;
				nearest = cover;
			}
		}

		return nearest;
	}
	
	public override void _PhysicsProcess(double delta)
	{
		UpdateTarget();
		UpdateEnemyState();

		if (currentState == EnemyState.InCombat)
		{
			UpdateCombatMovement(delta);
		}
		else if (currentState == EnemyState.HuntingTeam)
		{
			UpdateHuntMovement();
		}
		
		if (currentState != EnemyState.InCombat)
		{
			LookMovement(delta);
		}
		
		if (currentState == EnemyState.HuntingTeam && huntTimer <= 0f)
		{
			forcedHunt = false;
			PickRandomCapturePoint();
			currentState = EnemyState.GoingToCapture;
		}

		EnemyMovement(delta);
		LookAtTargetOrDirection(delta);
		Shooting();
		
		if (LinearVelocity.LengthSquared() < 0.05f)
		{
			stuckTimer += (float)delta;
		}
		else
		{
			stuckTimer = 0f;
		}

		if (stuckTimer > 2f && currentState == EnemyState.GoingToCapture)
		{
			PickRandomCapturePoint();
			stuckTimer = 0f;
		}
	}
	
	private void PickRandomCapturePoint()
	{
		if (capturePoints.Count == 0)
			return;

		currentCapturePoint = capturePoints[rng.RandiRange(0, capturePoints.Count - 1)];

		Vector3 spread = new Vector3(
			rng.RandfRange(-CapturePointSpread, CapturePointSpread),
			0,
			rng.RandfRange(-CapturePointSpread, CapturePointSpread)
		);

		MovementEnemy.TargetPosition = currentCapturePoint.GlobalPosition + spread;
	}
	
	private void UpdateTarget()
	{
		if (currentState == EnemyState.HuntingTeam)
		{
			Node3D seen = GetRaycastTeam();
			if (seen != null)
				currentTarget = seen;

			return;
		}

		currentTarget = null;

		var bodies = EnemyDetection.GetOverlappingBodies();
		float nearestDist = float.MaxValue;

		foreach (var body in bodies)
		{
			if (body is Node3D node && node.IsInGroup("Team"))
			{
				float dist = GlobalPosition.DistanceTo(node.GlobalPosition);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					currentTarget = node;
				}
			}
		}
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
	
	private void LookMovement(double delta)
	{
		Vector3 velocity = LinearVelocity;
		velocity.Y = 0;

		if (velocity.LengthSquared() < 0.01f)
			return;

		Vector3 lookDir = -velocity.Normalized();
		float targetYaw = Mathf.Atan2(lookDir.X, lookDir.Z);

		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, targetYaw, rotationSpeed * (float)delta);
		Rotation = rot;
	}
	
	private async void Shooting()
	{
		if (isReloading || isShooting)
			return;

		if (AllyInSight())
			return;

		if (currentTarget != null && TargetWithinDetection(currentTarget))
		{
			await TryShoot();
			return;
		}

		Node3D rayTarget = GetRaycastTeam();
		if (rayTarget != null)
		{
			currentTarget = rayTarget;
			await TryShoot();
		}
	}
	
	private async Task TryShoot()
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
	
	private bool TargetWithinDetection (Node3D target)
	{
		return GlobalPosition.DistanceTo(target.GlobalPosition) <= detectionRange;
	}

	private async Task Shoot()
	{
		currentAmmo--;
		Node3D bulletEnemySpawn = gunHolder.GetNodeOrNull<Node3D>($"{gunName}/BulletHole");
		BulletEnemy bulletEnemyInstance = bulletEnemyScene.Instantiate<BulletEnemy>();
		bulletEnemyInstance.GlobalTransform = bulletEnemySpawn.GlobalTransform;
		bulletEnemyInstance.SetGunType(gunName);
		bulletEnemyInstance.EnemyShooter = this;
		bulletEnemyInstance.Direction = -bulletEnemySpawn.GlobalTransform.Basis.Z;
		GetTree().CurrentScene.AddChild(bulletEnemyInstance);

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
			KillManager.Instance.AddTeamKill();
			Die();
		}
	}
	
	private bool IsLowHealth()
	{
		return CurrentHealth > 0 && CurrentHealth <= 50;
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
		EnemyDied?.Invoke(spawnMarker, gunName);
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
