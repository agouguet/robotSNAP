using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class TcpServer : MonoBehaviour
{
    [System.Serializable]
    public class SensorData
    {
        public float posX;
        public float posY;
        public float[] laser;
    }

    // Données partagées (thread-safe)
    private SensorData currentSensorData;
    private readonly object dataLock = new object();

    private TcpListener tcpListener;
    private Thread tcpListenerThread;
    private TcpClient connectedTcpClient;
    private bool isRunning = true;

    void Start()
    {
        // Initialiser les données par défaut
        lock (dataLock)
        {
            currentSensorData = new SensorData();
        }

        // Démarrer le serveur dans un thread séparé
        tcpListenerThread = new Thread(new ThreadStart(ListenForClients));
        tcpListenerThread.IsBackground = true;
        tcpListenerThread.Start();
    }

    void Update()
    {
        // Collecte des données dans le thread principal (Update est appelé sur le main thread)
        // Ici, vous pouvez lire votre robot, les capteurs, etc.
        SensorData newData = new SensorData();
        newData.posX = transform.position.x;
        newData.posY = transform.position.y;
        
        // Exemple de données laser (à remplacer par vos vraies données)
        newData.laser = new float[] { 0.1f, 0.5f, 0.9f };

        // Mise à jour thread-safe des données partagées
        lock (dataLock)
        {
            currentSensorData = newData;
        }
    }

    private void ListenForClients()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, 25000);
            tcpListener.Start();
            Debug.Log("Serveur Unity démarré sur le port 25000");

            while (isRunning)
            {
                connectedTcpClient = tcpListener.AcceptTcpClient();
                Debug.Log("Client Python connecté !");
                HandleClient(connectedTcpClient);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Erreur serveur: " + e.Message);
        }
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        int bytesRead;

        while (client.Connected)
        {
            // 1. Recevoir les commandes Python
            if (stream.DataAvailable)
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Debug.Log("Commande reçue: " + receivedMessage);
                // Traitez la commande ici (attention, vous êtes dans un thread secondaire)
                // Vous pouvez utiliser une queue pour envoyer la commande au main thread via une
                // coroutine ou un événement. Pour l'instant, on l'ignore.
            }

            // 2. Envoyer les données des capteurs (copie locale thread-safe)
            SensorData dataToSend;
            lock (dataLock)
            {
                // Faire une copie pour éviter que l'objet ne soit modifié pendant la sérialisation
                dataToSend = new SensorData
                {
                    posX = currentSensorData.posX,
                    posY = currentSensorData.posY,
                    laser = (float[])currentSensorData.laser?.Clone() // Cloner le tableau
                };
            }

            string jsonData = JsonUtility.ToJson(dataToSend);
            byte[] dataBytes = Encoding.UTF8.GetBytes(jsonData + "\n");
            stream.Write(dataBytes, 0, dataBytes.Length);
            stream.Flush();

            Thread.Sleep(20); // 50 Hz
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        tcpListener?.Stop();
        connectedTcpClient?.Close();
        tcpListenerThread?.Join(1000); // Attendre la fin du thread
    }
}