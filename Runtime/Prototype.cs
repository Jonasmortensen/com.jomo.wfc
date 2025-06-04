using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Jomo.WFC
{
    public enum NeighbourDirection {POSITIVE_X, NEGATIVE_X, POSITIVE_Z, NEGATIVE_Z}

    [Serializable]
    //A socket is a string identifier. Two prototypes with identical sockets in opposing directions are considered valid neighbours.
    //Prototypes currently have four sockets (no neighbours in Y)
    //TODO: I want to support six sockets
    public class SocketList
    {
        public string GetSocketInDirection(NeighbourDirection direction, bool reverse = false)
        {
            switch (direction)
            {
                case NeighbourDirection.POSITIVE_X:
                    return reverse ? posXRev : posX;
                case NeighbourDirection.NEGATIVE_X:
                    return reverse ? negXRev : negX;
                case NeighbourDirection.POSITIVE_Z:
                    return reverse ? posZRev : posZ;
                case NeighbourDirection.NEGATIVE_Z:
                    return reverse ? negZRev : negZ;
            }
        
        
            throw new Exception("Invalid neighbour direction");
        }
    
        public string posX;
        public string negZ;
        public string negX;
        public string posZ;

        private string posXRev;
        private string negZRev;
        private string negXRev;
        private string posZRev;
    
        public static SocketList SwizzleSockets(SocketList sockets)
        {
            SocketList newSockets = new SocketList();
            newSockets.posX = sockets.posZ;
            newSockets.negZ = sockets.posX;
            newSockets.negX = sockets.negZ;
            newSockets.posZ = sockets.negX;
            newSockets.AllocateReverseSockets();
        
            return newSockets;
        }
    
        public void AllocateReverseSockets()
        {
            posXRev = Reverse(posX);
            negZRev = Reverse(negZ);
            negXRev = Reverse(negX);
            posZRev = Reverse(posZ);
        }
    
    
        public static string Reverse( string s )
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
    
    }

//A prototype represents a tile. Rotations around z-axis is possible
    [Serializable]
    public class Prototype
    { 
        //The mesh too instantiate for this prototype
        public string mesh_name;
        //Indicates that a mesh should be rotated 90*rotation degrees when instantiated
        public int rotation;
    
    
    
        public int id; //TODO: Is this used?
        public SocketList sockets;
        public float weight; //More weight means higher chance of collapse

    
        public static Prototype MakeCopy(Prototype original)
        {
            Prototype copy = new Prototype();
            copy.mesh_name = original.mesh_name;
            copy.rotation = original.rotation;
            copy.id = original.id;
            copy.sockets = original.sockets; //Handle copy?
            //copy.posXNeighbor = new List<int>(original.posXNeighbor);
            //copy.negZNeighbor = new List<int>(original.negZNeighbor);
            //copy.negXNeighbor = new List<int>(original.negXNeighbor);
            //copy.posZNeighbor = new List<int>(original.posZNeighbor);
            copy.weight = original.weight;
            return copy;
        }

        /*
        public static bool CheckSocketEqual(string socket1, string socket2)
        {
            Profiler.BeginSample("Checking socket equality");
            bool result = socket1 == socket2;
            Profiler.EndSample();
            return result;
        }
        */

        public static Prototype[] PrototypesFromJsom(string jsonFile, bool addRotations)
        {
            Prototype[] prototypes = JsonUtility.FromJson<PrototypeList>(jsonFile).prototypes;

            int id = 0;

            foreach (Prototype prototype in prototypes)
            {
                prototype.id = id;
                id++;
            
                prototype.sockets.AllocateReverseSockets();
            
            }

            if (addRotations) prototypes = AddRotations(prototypes);
        
            return prototypes;
        }
    

        //Adds three additional rotations of each prototype
        public static Prototype[] AddRotations(Prototype[] prototypes)
        {
            Prototype[] newPrototypes = new Prototype[prototypes.Length * 4];

            int id = prototypes.Length;

            for (int i = 0; i < prototypes.Length; i++)
            {
                newPrototypes[i] = prototypes[i];
            
                Prototype p = MakeCopy(prototypes[i]);
                for (int j = 0; j < 3; j++)
                {
                    p = MakeCopy(p);
                    p.id = id;
                    p.rotation = j+1;
                    p.sockets = SocketList.SwizzleSockets(p.sockets);
                    newPrototypes[id] = p;
                    id++;
                }
            }
            Debug.Log("Final ID: " + id);
            return newPrototypes;
        }
    
    
    }

    [Serializable]
    public class PrototypeList
    {
        public Prototype[] prototypes;
    }
}