#if ENABLE_INPUT_SYSTEM
#define NEW_INPUT
using UnityEngine.InputSystem;
#endif

using System.Collections;
using UnityEngine;

public enum CharacterState
{
	Idle,
	Box
};

public class CrafterControllerFREE:MonoBehaviour
{
	private Animator animator;
	private GameObject box;
	private readonly float rotationSpeed = 5;
	private Vector3 inputVec;
	private bool isMoving;
	private bool isLocked;
	public CharacterState charState;

	private void Awake()
	{
		animator = GetComponent<Animator>();
		box = GameObject.Find("Carry");
	}

	private void Start()
	{
		StartCoroutine(_ShowItem("none", 0f));
		charState = CharacterState.Idle;
	}

	private void Update()
	{
		float x = 0f;
		float z = 0f;

		// New Input System.
		#if ENABLE_INPUT_SYSTEM
		if (Keyboard.current != null) {
			z = Keyboard.current.dKey.isPressed ? 1f : Keyboard.current.aKey.isPressed ? -1f : 0f;
			x = -(Keyboard.current.wKey.isPressed ? 1f : Keyboard.current.sKey.isPressed ? -1f : 0f);
		}
		// Legacy Input Manager.
		#else
		z = Input.GetAxisRaw("Horizontal");
		x = -Input.GetAxisRaw("Vertical");

		#endif

		inputVec = new Vector3(x, 0, z);
		animator.SetFloat("VelocityX", -x);
		animator.SetFloat("VelocityY", z);

		// If there is some input.
		if (x != 0 || z != 0) {
			// Set that character is moving.
			animator.SetBool("Moving", true);
			isMoving = true;
		}
		// Character is not moving.
		else {
			animator.SetBool("Moving", false);
			isMoving = false;
		}

		// Update character position and facing.
		UpdateMovement();

		// Reset Crafter.
		if (InputHelper.GetKey(KeyCode.R)) {
			gameObject.transform.position = new Vector3(0, 0, 0);
		}

		// Send velocity to animator.
		animator.SetFloat("Velocity", UpdateMovement());
	}

	// Face character along input direction.
	private void RotateTowardsMovementDir()
	{
		if (!isLocked) {
			if (inputVec != Vector3.zero) {
				transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(inputVec), Time.deltaTime * rotationSpeed);
			}
		}
	}

	private float UpdateMovement()
	{
		// Get movement input from controls.
		Vector3 motion = inputVec;

		// Reduce input for diagonal movement.
		motion *= (Mathf.Abs(inputVec.x) == 1 && Mathf.Abs(inputVec.z) == 1) ? 0.7f : 1;

		// If not paused, face character along input direction.
		if (!isLocked) {
			RotateTowardsMovementDir();
		}

		return inputVec.magnitude;
	}

	private void OnGUI()
	{
		if (charState == CharacterState.Idle && !isMoving) {
			if (GUI.Button(new Rect(25, 25, 150, 30), "Pickup Box")) {
				animator.SetTrigger("CarryPickupTrigger");
				StartCoroutine(_MoveLock(1.2f));
				StartCoroutine(_ShowItem("box", .5f));
				charState = CharacterState.Box;
			}
			if (GUI.Button(new Rect(25, 65, 150, 30), "Recieve Box")) {
				animator.SetTrigger("CarryRecieveTrigger");
				StartCoroutine(_MoveLock(1.2f));
				StartCoroutine(_ShowItem("box", .5f));
				charState = CharacterState.Box;
			}
		}
		if (charState == CharacterState.Box && !isMoving) {
			if (GUI.Button(new Rect(25, 25, 150, 30), "Put Down Box")) {
				animator.SetTrigger("CarryPutdownTrigger");
				StartCoroutine(_MoveLock(1.2f));
				StartCoroutine(_ShowItem("none", .7f));
				charState = CharacterState.Idle;
			}
			if (GUI.Button(new Rect(25, 65, 150, 30), "Give Box")) {
				animator.SetTrigger("CarryHandoffTrigger");
				StartCoroutine(_MoveLock(1.2f));
				StartCoroutine(_ShowItem("none", .6f));
				charState = CharacterState.Idle;
			}
		}
	}

	public IEnumerator _MoveLock(float pauseTime)
	{
		isLocked = true;
		yield return new WaitForSeconds(pauseTime);
		isLocked = false;
	}

	public IEnumerator _ChangeCharacterState(float waitTime, CharacterState state)
	{
		yield return new WaitForSeconds(waitTime);
		charState = state;
	}

	public IEnumerator _ShowItem(string item, float waittime)
	{
		yield return new WaitForSeconds(waittime);

		if (item == "none") {
			box.SetActive(false);
		}
		else if (item == "box") {
			box.SetActive(true);
		}

		yield return null;
	}
}