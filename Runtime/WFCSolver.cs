using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Random = UnityEngine.Random;


namespace Jomo.WFC
{
    public class WFCSolver
    {
        public int loopCount = 0;
    
        public class SuperPosition
        {
            //The possible prototypes
            public List<int> m_PrototypeIndices;

            private SuperPosition m_posX;
            private SuperPosition m_negX;
            private SuperPosition m_posZ;
            private SuperPosition m_negZ;

            //TODO: Not sure if both should be set
            public void SetNeighbour(NeighbourDirection direction, SuperPosition neighbour)
            {
                switch (direction)
                {
                    case NeighbourDirection.POSITIVE_X:
                        m_posX = neighbour;
                        break;
                    case NeighbourDirection.NEGATIVE_X:
                        m_negX = neighbour;
                        break;
                    case NeighbourDirection.POSITIVE_Z:
                        m_posZ = neighbour;
                        break;
                    case NeighbourDirection.NEGATIVE_Z:
                        m_negZ = neighbour;
                        break;
                }
            }

            public SuperPosition GetNeighbour(NeighbourDirection direction)
            {
                switch (direction)
                {
                    case NeighbourDirection.POSITIVE_X:
                        return m_posX;
                    case NeighbourDirection.NEGATIVE_X:
                        return m_negX;
                    case NeighbourDirection.POSITIVE_Z:
                        return m_posZ;
                    case NeighbourDirection.NEGATIVE_Z:
                        return m_negZ;
                }

                throw new Exception("Invalid neighbour direction");
            }

            public bool HasNullNeighbour()
            {
                return m_posX == null || m_negX == null || m_posZ == null || m_negZ == null;
            }

            public int NullNeightbourCount()
            {
                int count = 0;
                if (m_posX != null) count++;
                if(m_negX != null) count++;
                if (m_posZ != null) count++;
                if (m_negZ != null) count++;
                return count;
            }
        }

        public List<SuperPosition> m_SuperPositions;

        private int m_Width, m_Height;
        private Prototype[] m_Prototypes;
        private TileConnectionGraph m_ConnectionGraph;

        public Action<SuperPosition, WFCSolver> m_CustomCollapse;

        static readonly ProfilerMarker s_PropegatePerfMarker = new ProfilerMarker("WFCSolver.Propegate");
        static readonly ProfilerMarker s_GetNeighboursPerfMarker = new ProfilerMarker("WFCSolver.GetPossibleNeighbours");


        //memory optimization
        private List<int> m_NeighboursUnionCache = new List<int>(100);


        //Lookup table for getting prototypes that fits with given socket in given direction
        private Dictionary<(string, NeighbourDirection), Prototype[]> m_NeighbourLookup;

        public bool Solve(SuperPosition pos, int prototypeID)
        {
            if (pos.m_PrototypeIndices.Count == 1)
            {
                if (pos.m_PrototypeIndices[0] != prototypeID)
                {
                    Debug.Log("Already collapsed to something else");
                }
                return true;
            }
            
            if(!pos.m_PrototypeIndices.Contains(prototypeID)) throw new Exception("Invalid collapse");
        
            pos.m_PrototypeIndices = new List<int> { prototypeID };

            return Propegate(pos);
        }

        public bool Solve(SuperPosition pos, List<int> prototypeIDs)
        {
            var validPrototypes = new List<int>();

            foreach (var p in prototypeIDs)
            {
                if(pos.m_PrototypeIndices.Contains(p)) validPrototypes.Add(p);
            }

            if (validPrototypes.Count == 0)
            {
                Debug.Log("No valid prototypes found");
                return false;
            }

            int prototypeIndex = PickWeightedPrototype(validPrototypes);
        
            return Solve(pos, prototypeIndex);
        }

        public bool Solve(SuperPosition pos)
        {
            int prototypeIndex = PickWeightedPrototype(pos.m_PrototypeIndices);
        
            return Solve(pos, prototypeIndex);
        }

        /// <summary>
        /// Collapses the given superpositions in to prototypes from the given list
        /// </summary>
        /// <param name="superPositions"></param>
        /// <param name="prototypes"></param>
        public bool Solve(List<SuperPosition> superPositions, List<int> prototypes)
        {
            foreach (var superPosition in superPositions)
            {
                if(!Solve(superPosition, prototypes)) return false;
            }

            return true;
        }

        /// <summary>
        /// Removes the given prototypes from the given superpositoins. Does not propegate
        /// </summary>
        public void Forbid(List<SuperPosition> superPositions, List<int> prototypes)
        {
            foreach (var superPosition in superPositions)
            {
                if (superPosition.m_PrototypeIndices.Count(p => prototypes.Contains(p)) ==
                    superPosition.m_PrototypeIndices.Count)
                {
                    Debug.LogWarning("Skipped forbidding because it would leave super position empty");
                    continue;
                }
            
            
                superPosition.m_PrototypeIndices.RemoveAll(p => prototypes.Contains(p));
            }
        }
    
        public WFCSolver(Prototype[] prototypes, TileConnectionGraph connectionGraph, string defaultSocket = "")
        {
            m_ConnectionGraph = connectionGraph;

            SuperPosition[] superPositions = new SuperPosition[m_ConnectionGraph.nodes.Count];

            m_Prototypes = prototypes;

            //Create all superpositions
            for (int i = 0; i < m_ConnectionGraph.nodes.Count; i++)
            {
                SuperPosition superPosition = new SuperPosition();
                superPosition.m_PrototypeIndices = Enumerable.Range(0, prototypes.Length).ToList();
                superPositions[m_ConnectionGraph.nodes[i].m_ID] = superPosition;
            }

            m_SuperPositions = superPositions.ToList();

            bool constrainOuterEdge = !string.IsNullOrEmpty(defaultSocket);
            List<SuperPosition> constrainedSuperPositions = new List<SuperPosition>();

            //Connect them remove and update prototypes of outer edge
            for (int i = 0; i < m_ConnectionGraph.nodes.Count; i++)
            {
                SuperPosition superPosition = m_SuperPositions[i];

                var node = m_ConnectionGraph.nodes[i];

                bool hasConstrained = false;

                if (node.m_xPos != null)
                {
                    superPosition.SetNeighbour(NeighbourDirection.POSITIVE_X, m_SuperPositions[node.m_xPos.m_ID]);
                }
                else if (constrainOuterEdge)
                {
                    superPosition.m_PrototypeIndices.RemoveAll(i => prototypes[i].sockets.posX != defaultSocket);
                    hasConstrained = true;
                }

                if (node.m_xNeg != null)
                {
                    superPosition.SetNeighbour(NeighbourDirection.NEGATIVE_X, m_SuperPositions[node.m_xNeg.m_ID]);
                }
                else if (constrainOuterEdge)
                {
                    superPosition.m_PrototypeIndices.RemoveAll(i => prototypes[i].sockets.negX != defaultSocket);
                    hasConstrained = true;
                }

                if (node.m_zPos != null)
                {
                    superPosition.SetNeighbour(NeighbourDirection.POSITIVE_Z, m_SuperPositions[node.m_zPos.m_ID]);
                }
                else if (constrainOuterEdge)
                {
                    superPosition.m_PrototypeIndices.RemoveAll(i => prototypes[i].sockets.posZ != defaultSocket);
                    hasConstrained = true;
                }

                if (node.m_zNeg != null)
                {
                    superPosition.SetNeighbour(NeighbourDirection.NEGATIVE_Z, m_SuperPositions[node.m_zNeg.m_ID]);
                }
                else if (constrainOuterEdge)
                {
                    superPosition.m_PrototypeIndices.RemoveAll(i => prototypes[i].sockets.negZ != defaultSocket);
                    hasConstrained = true;
                }

                if (hasConstrained) constrainedSuperPositions.Add(superPosition);
            }

            foreach (SuperPosition constrainedPos in constrainedSuperPositions)
            {
                Propegate(constrainedPos);
            }
        }

//Checks if every single superposition is collapsed
        //This could probably be done in constant time if I keep track of the amount of fully collapsed tiles
        private bool IsCollapsed()
        {
            foreach (var superPos in m_SuperPositions)
            {
                if (superPos.m_PrototypeIndices.Count != 1) return false;
            }

            return true;
        }


        private SuperPosition GetMinEntropyPosition()
        {
            int minEntropy = 1000;
            List<SuperPosition> candidates = new List<SuperPosition>();

            foreach (var superPos in m_SuperPositions)
            {
                int entropy = superPos.m_PrototypeIndices.Count;
                if (entropy == 1) continue;

                if (entropy < minEntropy)
                {
                    minEntropy = entropy;
                    candidates = new List<SuperPosition>();
                }

                if (entropy == minEntropy)
                {
                    candidates.Add(superPos);
                }
            }
        
            if(candidates.Count == 0) throw new Exception("Already collapsed");

            return candidates[Random.Range(0, candidates.Count)];
        }

        private List<int> GetPossibleNeighbours(SuperPosition superPosition, NeighbourDirection direction)
        {
            using (s_GetNeighboursPerfMarker.Auto())
            {
                //Find out which socket is facing me
                NeighbourDirection otherDirection;
                SuperPosition neighbour = superPosition.GetNeighbour(direction);
                if (neighbour.GetNeighbour(NeighbourDirection.POSITIVE_X) == superPosition)
                {
                    otherDirection = NeighbourDirection.POSITIVE_X;
                }
                else if (neighbour.GetNeighbour(NeighbourDirection.NEGATIVE_X) == superPosition)
                {
                    otherDirection = NeighbourDirection.NEGATIVE_X;
                }
                else if (neighbour.GetNeighbour(NeighbourDirection.POSITIVE_Z) == superPosition)
                {
                    otherDirection = NeighbourDirection.POSITIVE_Z;
                }
                else if (neighbour.GetNeighbour(NeighbourDirection.NEGATIVE_Z) == superPosition)
                {
                    otherDirection = NeighbourDirection.NEGATIVE_Z;
                }
                else
                {
                    throw new Exception("Could not find other direction");
                }

                //List<int> neightborsUnion = new List<int>(32);
            
                m_NeighboursUnionCache.Clear();

                //Foreach prototype
                for (int i = 0; i < superPosition.m_PrototypeIndices.Count; i++)
                {
                    Prototype prototype = m_Prototypes[superPosition.m_PrototypeIndices[i]];

                    string socket = prototype.sockets.GetSocketInDirection(direction);
                
                
                    //This takes time
                    //TODO: I could find this list in constant time if prototypes had a lookup table: (socket, direction) -> List<Prototype>
                    //Get all prototypes where socket in direction equals reverse socket in other direction
                
                
                    //Profiler.BeginSample("Looping prototypes");
                
                
                
                    for(int j = 0; j < m_Prototypes.Length; j++)
                    {
                        loopCount++;
                    
                        var otherPrototype = m_Prototypes[j];
                    
                        string reverseOtherSocket = otherPrototype.sockets.GetSocketInDirection(otherDirection, true);
                        bool fits = reverseOtherSocket == socket;
                    
                        if (fits)
                        {
                            m_NeighboursUnionCache.Add(otherPrototype.id);
                        
                        }
                    }
                
                    //Profiler.EndSample();
                }
                return m_NeighboursUnionCache;
            }

        
        }

        private void Collapse(SuperPosition superPosition)
        {
            if (m_CustomCollapse == null)
            {
                CollapseRandom(superPosition);
            }
            else
            {
                m_CustomCollapse.Invoke(superPosition, this);
            }
        }

        public void Clear(SuperPosition superPosition)
        {
            //Reset supoer position
            superPosition.m_PrototypeIndices = m_Prototypes.Select(p => p.id).ToList();
        }

        public void UpdatePosition(SuperPosition superPosition, int prototype)
        {
            superPosition.m_PrototypeIndices = new List<int> {prototype};

            RecalculateNeighbourPrototypes(superPosition);
        }

        public Prototype TryGetPrototype(SuperPosition superPosition)
        {
            if (superPosition.m_PrototypeIndices.Count != 1) return null;
            return m_Prototypes[superPosition.m_PrototypeIndices[0]];
        }

        public List<int> MeshNameToPrototypeIDs(string meshName)
        {
            List<int> result = new List<int>();
            for (int i = 0; i < m_Prototypes.Length; i++)
            {
                if(m_Prototypes[i].mesh_name == meshName) result.Add(i);
            }

            return result;
        }

        private int PickWeightedPrototype(List<int> prototypes)
        {
            //Find total weight sum
            float weightSum = 0;
            for (int i = 0; i < prototypes.Count; i++)
            {
                weightSum += m_Prototypes[prototypes[i]].weight;
            }
        
            if (weightSum == 0)
            {
                int uniformDraw = Random.Range(0, prototypes.Count);
                return prototypes[uniformDraw];
            }
        
            //Make draw and reset sum
            float draw = Random.Range(0, weightSum);
            weightSum = 0;

            //find index of band
            int selection = 0;
            for (int i = 0; i < prototypes.Count; i++)
            {
                weightSum += m_Prototypes[prototypes[i]].weight;
                if (draw < weightSum)
                {
                    selection = i;
                    break;
                }
            }

            return prototypes[selection];
        }


        private bool ValidateNeighbour(SuperPosition superPosition, NeighbourDirection direction)
        {
            SuperPosition neighbour = superPosition.GetNeighbour(direction);

            if (neighbour == null) return true;
        
        
            //Try updating in one direction
            List<int> validNeighbourPrototypes = GetPossibleNeighbours(superPosition, direction);
            List<int> intersection = new List<int>();

            foreach (var p in neighbour.m_PrototypeIndices)
            {
                if (validNeighbourPrototypes.Contains(p))
                {
                    intersection.Add(p);
                }
            }

            if (intersection.Count > 0)
            {
                neighbour.m_PrototypeIndices = intersection;
                return true;
            }
        
        
            //TODO: I need to check the other way and remove?
        
            neighbour.m_PrototypeIndices = validNeighbourPrototypes;
            return false;
        }
    
        //Returns whether position is valid
        private void RecalculateNeighbourPrototypes(SuperPosition root)
        {
            Stack<SuperPosition> stack = new Stack<SuperPosition>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();

                if (!ValidateNeighbour(current, NeighbourDirection.POSITIVE_X))
                {
                    if (!stack.Contains(current.GetNeighbour(NeighbourDirection.POSITIVE_X)))
                    {
                        stack.Push(current.GetNeighbour(NeighbourDirection.POSITIVE_X));
                    }
                }
            
                if (!ValidateNeighbour(current, NeighbourDirection.NEGATIVE_X))
                {
                    if (!stack.Contains(current.GetNeighbour(NeighbourDirection.POSITIVE_X)))
                    {
                        stack.Push(current.GetNeighbour(NeighbourDirection.POSITIVE_X));
                    }
                }
            
                if (!ValidateNeighbour(current, NeighbourDirection.POSITIVE_Z))
                {
                    if (!stack.Contains(current.GetNeighbour(NeighbourDirection.POSITIVE_X)))
                    {
                        stack.Push(current.GetNeighbour(NeighbourDirection.POSITIVE_X));
                    }
                }
            
                if (!ValidateNeighbour(current, NeighbourDirection.NEGATIVE_Z))
                {
                    if (!stack.Contains(current.GetNeighbour(NeighbourDirection.POSITIVE_X)))
                    {
                        stack.Push(current.GetNeighbour(NeighbourDirection.POSITIVE_X));
                    }
                }
            }
        }

        /*
        public bool RecalculatePrototypes(SuperPosition pos)
        {
            List<int> posX = GetPossibleNeighbours(pos, NeighbourDirection.POSITIVE_X);
            List<int> posZ = GetPossibleNeighbours(pos, NeighbourDirection.POSITIVE_Z);
            List<int> negX = GetPossibleNeighbours(pos, NeighbourDirection.NEGATIVE_X);
            List<int> negZ = GetPossibleNeighbours(pos, NeighbourDirection.NEGATIVE_Z);

            List<int> intersection = new List<int>();

            foreach (var p in posX)
            {
                if (posZ.Contains(p) && negZ.Contains(p) && negX.Contains(p))
                {
                    intersection.Add(p);
                }
            }

            if (intersection.Count == 0) return false;

            pos.m_PrototypeIndices = intersection;
            return true;
        }
        */

        private void CollapseRandom(SuperPosition superPosition)
        {
            var prototypes = superPosition.m_PrototypeIndices;
        
            if (prototypes.Count == 1) throw new Exception("Superposition already collapsed");

            superPosition.m_PrototypeIndices = new List<int> { PickWeightedPrototype(prototypes) };
        }

        //This method validates the neighbour superposition and returns it if it was updated
        private (bool, SuperPosition) UpdateNeighbour(SuperPosition superPosition, NeighbourDirection neighbourDirection)
        {
            //Neighboring super position
            SuperPosition neighbour = superPosition.GetNeighbour(neighbourDirection);

            //If no neighbour. Nothing to validate
            if (neighbour == null) return (true, null);

            //Get possible prototypes in direction
            List<int> possiblePrototypes = GetPossibleNeighbours(superPosition, neighbourDirection);
        
            if (neighbour.m_PrototypeIndices.Count == 1)
            {
                if(possiblePrototypes.Contains(neighbour.m_PrototypeIndices[0])) return (true, null); //Neighbour already collapsed to a valid prototype, leave it
            }
        
            //Find out what to ban
            List<int> toBan = new List<int>();
        
            //Check if each prototype in neighbour super position is still valid
            foreach (var id in neighbour.m_PrototypeIndices)
            {
                //If it is no longer a valid neightbour, mark it for constraint
                //O(n)
                if (!possiblePrototypes.Contains(id))
                {
                    toBan.Add(id);
                }
            }

            bool neighbourConstrained = toBan.Count > 0;
        

            //Remove the prototypes from the neighbour superposition
            foreach (var id in toBan)
            {
                if (neighbour.m_PrototypeIndices.Count <= 1)
                {
                    Debug.Log("Banning in a collapsedPosition");
                    return (false, null); //Can't ban a collapsed position
                }
                neighbour.m_PrototypeIndices.Remove(id);
            }
        

            if (neighbourConstrained)
            {
                return (true, neighbour);
            }
        
            return (true, null);
        }

        //Update all neighbour lists starting with the root super position
        public bool Propegate(SuperPosition rootSuperPos)
        {
            using (s_PropegatePerfMarker.Auto())
            {
                Stack<SuperPosition> stack = new Stack<SuperPosition>();

                stack.Push(rootSuperPos);

        
                while (stack.Count > 0)
                {
                    SuperPosition currentSuperPos = stack.Pop();

                    //If a neighbour has been updated the changes need to propegate

                    var (success, updatedNeighobur) = UpdateNeighbour(currentSuperPos, NeighbourDirection.POSITIVE_X);
                    if (updatedNeighobur != null) stack.Push(updatedNeighobur);
                    if (!success) return false;

                    (success, updatedNeighobur) = UpdateNeighbour(currentSuperPos, NeighbourDirection.NEGATIVE_X);
                    if (updatedNeighobur != null) stack.Push(updatedNeighobur);
                    if (!success) return false;

                    (success, updatedNeighobur) = UpdateNeighbour(currentSuperPos, NeighbourDirection.POSITIVE_Z);
                    if (updatedNeighobur != null) stack.Push(updatedNeighobur);
                    if (!success) return false;

                    (success, updatedNeighobur) = UpdateNeighbour(currentSuperPos, NeighbourDirection.NEGATIVE_Z);
                    if (updatedNeighobur != null) stack.Push(updatedNeighobur);
                    if (!success) return false;
                }
            }

            return true;

        }

        public bool Iterate()
        {
            var superPosition = GetMinEntropyPosition();

            Collapse(superPosition);
            return Propegate(superPosition);
        }

        //Run the wave function collapse and store the result in the original connection graph
        public bool Solve()
        {
            while (!IsCollapsed())
            {
                bool iterationSuccess = Iterate();
                if(!iterationSuccess) return false;
            }
        
            Debug.Log("Solved with " + loopCount + " iterations");

            return true;
        }

        public void WritePrototypesToGraph()
        {
            for (int i = 0; i < m_SuperPositions.Count; i++)
            {
                if (m_SuperPositions[i].m_PrototypeIndices.Count == 1)
                {
                    m_ConnectionGraph.nodes[i].m_Prototype = m_Prototypes[m_SuperPositions[i].m_PrototypeIndices[0]];
                } 
            
            }
        }
    
    }
}