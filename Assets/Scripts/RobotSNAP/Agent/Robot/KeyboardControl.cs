using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
///     This script detects keyboard controls (arrow keys only)
///     and use them to control the mobile robot
/// </summary>
public class KeyboardControl : MonoBehaviour
{
    public ArticulationWheelController wheelController;

    public float speed = 1.5f;
    public float angularSpeed = 1.5f;
    private float targetLinearSpeed;
    private float targetAngularSpeed;

    void Start() {}

    void Update()
    {
        // Initialisation
        float forward = 0f;
        float turn = 0f;

        // Flèches haut/bas pour avancer/reculer
        if (Input.GetKey(KeyCode.UpArrow))
            forward = 1f;
        else if (Input.GetKey(KeyCode.DownArrow))
            forward = -1f;

        // Flèches gauche/droite pour tourner
        if (Input.GetKey(KeyCode.LeftArrow))
            turn = 1f;   // ou -1 selon orientation
        else if (Input.GetKey(KeyCode.RightArrow))
            turn = -1f;  // ajuster le signe selon besoin

        targetLinearSpeed = forward * speed;
        targetAngularSpeed = turn * angularSpeed;
    }

    void FixedUpdate()
    {
        wheelController.SetRobotVelocity(targetLinearSpeed, targetAngularSpeed);
    }
}