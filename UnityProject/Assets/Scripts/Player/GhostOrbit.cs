using System.Threading.Tasks;
using Objects;
using UnityEngine;
using Mirror;


namespace Player
{
	public class GhostOrbit : NetworkBehaviour
	{
		public static GhostOrbit Instance;

		[SyncVar(hook = nameof(SyncOrbitObject))]
		private NetworkIdentity IDtarget;

		private GameObject target
		{
			get => IDtarget.OrNull()?.gameObject;
			set => SyncOrbitObject(IDtarget, value.NetWorkIdentity());
		}


		[SerializeField] private GhostMove GhostMove;
		[SerializeField] private RotateAroundTransform rotateTransform;

		/// <summary>
		/// Time in milliseconds! The time between mouse clicks where we can orbit an object
		/// </summary>
		private readonly int doubleClickTime = 500;
		private bool hasClicked = false;

		private void Start()
		{
			if (GhostMove == null) GhostMove = GetComponent<GhostMove>();
			if (rotateTransform == null) rotateTransform = GetComponent<RotateAroundTransform>();
			UpdateManager.Add(CallbackType.UPDATE, UpdateMe);
			Instance = this;
		}

		private void OnDisable()
		{
			UpdateManager.Remove(CallbackType.UPDATE, UpdateMe);

			if(CustomNetworkManager.IsServer == false) return;
			StopOrbiting();
		}

		private void SyncOrbitObject(NetworkIdentity oldObject, NetworkIdentity newObject)
		{
			IDtarget = newObject;

			if (target == null)
			{
				ResetRotate();
				return;
			}

			rotateTransform.TransformToRotateAround = target.transform;
		}

		private void UpdateMe()
		{
			if(isLocalPlayer == false) return;

			if (Input.GetMouseButtonDown(0))
			{
				if (hasClicked == false)
				{
					DoubleClickTimer();
					return;
				}
				FindObjectToOrbitUnderMouse();
			}
			if (KeyboardInputManager.IsMovementPressed())
			{
				CmdStopOrbiting();
			}
		}

		private void FindObjectToOrbitUnderMouse()
		{
			var possibleTargets = MouseUtils.GetOrderedObjectsUnderMouse();
			foreach (var possibleTarget in possibleTargets)
			{
				if (possibleTarget.TryGetComponent<UniversalObjectPhysics>(out var pull) || possibleTarget.TryGetComponent<Singularity>(out var loose))
				{
					CmdServerOrbit(possibleTarget);
					return;
				}
			}
		}

		private async void DoubleClickTimer()
		{
			hasClicked = true;
			await Task.Delay(doubleClickTime).ConfigureAwait(false);
			hasClicked = false;
		}

		[Server]
		private void Orbit(GameObject thingToOrbit)
		{
			if(thingToOrbit == null) return;
			target = thingToOrbit;

			var WorldMove = target.AssumedWorldPosServer();
			var Matrix = MatrixManager.AtPoint(WorldMove, isServer);
			GhostMove.ForcePositionClient( WorldMove.ToLocal(Matrix), Matrix.Id, OrientationEnum.Down_By180);

			UpdateManager.Add(FollowTarget, 0.1f);
			Chat.AddExamineMsg(gameObject, $"You start orbiting {thingToOrbit.ExpensiveName()}");
		}

		[Server]
		private void StopOrbiting()
		{
			if(target == null) return;

			Chat.AddExamineMsg(gameObject, $"You stop orbiting {target.ExpensiveName()}");
			target = null;
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, FollowTarget);
			ResetRotate();
		}

		private void ResetRotate()
		{
			rotateTransform.TransformToRotateAround = null;

			var rotateTransformCache = rotateTransform.transform;
			rotateTransformCache.up = Vector3.zero;
			rotateTransformCache.localPosition = Vector3.zero;
		}

		[Command]
		public void CmdStopOrbiting()
		{
			if(target == null) return;
			StopOrbiting();
		}

		/// <summary>
		/// Mirror does not support IEnumerable so we cannot turn the FindObjectToOrbit function into a command.
		/// </summary>
		[Command]
		public void CmdServerOrbit(GameObject thingToOrbit)
		{
			Orbit(thingToOrbit);
		}

		//This function is only really here to make sure the server keeps the ghost tile position correct
		//TODO: Might be worth changing this to be called from the target CNT OnTileReached instead?
		private void FollowTarget()
		{
			if (target == null) return;


			var WorldMove = target.AssumedWorldPosServer();
			var Matrix = MatrixManager.AtPoint(WorldMove, isServer);
			GhostMove.ForcePositionClient( WorldMove.ToLocal(Matrix), Matrix.Id, OrientationEnum.Down_By180);



			if (target.AssumedWorldPosServer() == TransformState.HiddenPos)
			{
				//In closet so cancel orbit for clients
				StopOrbiting();
			}
		}
	}
}
