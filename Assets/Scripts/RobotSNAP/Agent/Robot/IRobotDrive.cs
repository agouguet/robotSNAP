namespace RobotSNAP.Agents
{
    /// <summary>
    /// Ce qui sait transformer une consigne de vitesse (m/s, rad/s) en mouvement du robot. Un châssis à roues
    /// le fait par ses articulations, un châssis sans roue (un humanoïde, un mesh quelconque) le fait en
    /// déplaçant sa base. Le reste de l'application ne connaît que cette interface.
    /// </summary>
    public interface IRobotDrive
    {
        /// <summary>Consigne de vitesse linéaire (m/s) et angulaire (rad/s), dans le repère du robot.</summary>
        void SetRobotVelocity(float linearSpeed, float angularSpeed);

        /// <summary>Remet le châssis à zéro après un téléport, comme le ferait un robot qu'on repose au sol.</summary>
        void ResetDrives();
    }
}
