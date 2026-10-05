using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;


namespace Jomo.WFC
{
    public class WFCSolver
    {
        public class SuperPosition
        {
            //The possible prototypes
            public List<int> m_PrototypeIndices;

            // The graph node this superposition solves. For graphs made with TileConnectionGraph.FromMesh it is a
            // MeshNode, whose m_Face gives access to the mesh face and its data.
            public Node m_Node;

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


        // For a socket and the direction a neighbour faces back with, the ids of every prototype whose socket in that
        // direction, reversed, equals it: the prototypes that fit next to a side with that socket. Built once per solver.
        private Dictionary<(string, NeighbourDirection), int[]> m_NeighbourLookup;

        // Which prototype ids fit towards the neighbour last looked at (see MarkPossibleNeighbours), the ids set in it,
        // and the sockets already looked up. Reused, so propagating doesn't allocate.
        private bool[] m_Allowed;
        private readonly List<int> m_AllowedIds = new List<int>();
        private readonly HashSet<string> m_SeenSockets = new HashSet<string>();

        // Positions that may still be uncollapsed, in m_SuperPositions order, so finding the next one to collapse
        // only looks at those. Null when it has to be rebuilt.
        private List<SuperPosition> m_Open;
        private readonly List<SuperPosition> m_MinEntropyCandidates = new List<SuperPosition>();

        // Why propagation last failed, or null if it hasn't: the position that ran out of prototypes, and the sockets a
        // tile there would need on each side to fit what its neighbours can still be. Adding a prototype with those
        // sockets (in any rotation, if rotations are generated) would have avoided the contradiction.
        public string LastContradiction { get; private set; }

        // Set when the border sockets already rule out every prototype somewhere, so Solve fails straight away
        private bool m_Unsolvable;

        // Socket for every side without a neighbour, or null
        private string m_DefaultSocket;

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
                LastContradiction = "None of the requested prototypes are still possible at node " +
                                    (pos.m_Node != null ? pos.m_Node.m_ID.ToString() : "?") + ".\n" + DescribeContradiction(pos);
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
            var forbidden = new HashSet<int>(prototypes);
            foreach (var superPosition in superPositions)
            {
                if (superPosition.m_PrototypeIndices.Count(p => forbidden.Contains(p)) ==
                    superPosition.m_PrototypeIndices.Count)
                {
                    Debug.LogWarning("Skipped forbidding because it would leave super position empty");
                    continue;
                }
            
            
                superPosition.m_PrototypeIndices.RemoveAll(p => forbidden.Contains(p));
            }
        }
    
        // Every random choice the solver makes comes from here, including those a custom collapse should make, so a
        // solve with the same seed, prototypes and graph always gives the same result
        public System.Random RandomSource { get; }

        // With a seed the solve is repeatable. Without one it is seeded differently every time.
        public WFCSolver(Prototype[] prototypes, TileConnectionGraph connectionGraph, string defaultSocket = "", int? seed = null)
        {
            RandomSource = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            m_ConnectionGraph = connectionGraph;

            SuperPosition[] superPositions = new SuperPosition[m_ConnectionGraph.nodes.Count];

            m_Prototypes = prototypes;
            BuildNeighbourLookup();

            //Create all superpositions
            for (int i = 0; i < m_ConnectionGraph.nodes.Count; i++)
            {
                SuperPosition superPosition = new SuperPosition();
                superPosition.m_PrototypeIndices = Enumerable.Range(0, prototypes.Length).ToList();
                superPosition.m_Node = m_ConnectionGraph.nodes[i];
                superPositions[m_ConnectionGraph.nodes[i].m_ID] = superPosition;
            }

            m_SuperPositions = superPositions.ToList();

            bool constrainOuterEdge = !string.IsNullOrEmpty(defaultSocket);
            m_DefaultSocket = constrainOuterEdge ? defaultSocket : null;
            List<SuperPosition> constrainedSuperPositions = new List<SuperPosition>();

            //Connect them, and remove prototypes that don't fit the socket required on sides without a neighbour
            for (int i = 0; i < m_ConnectionGraph.nodes.Count; i++)
            {
                SuperPosition superPosition = m_SuperPositions[i];

                var node = m_ConnectionGraph.nodes[i];

                bool hasConstrained = false;

                foreach (NeighbourDirection direction in Enum.GetValues(typeof(NeighbourDirection)))
                {
                    Node neighbourNode = NeighbourNode(node, direction);
                    if (neighbourNode != null)
                    {
                        superPosition.SetNeighbour(direction, m_SuperPositions[neighbourNode.m_ID]);
                        continue;
                    }

                    string required = RequiredBorderSocket(node, direction);
                    if (required == null) continue;

                    superPosition.m_PrototypeIndices.RemoveAll(p => prototypes[p].sockets.GetSocketInDirection(direction) != required);
                    hasConstrained = true;
                }

                if (hasConstrained) constrainedSuperPositions.Add(superPosition);
            }

            // A position left without prototypes can't be solved, and propagating from it would go wrong
            SuperPosition empty = m_SuperPositions.FirstOrDefault(p => p.m_PrototypeIndices.Count == 0);
            if (empty != null)
            {
                m_Unsolvable = true;
                LastContradiction = DescribeContradiction(empty);
                return;
            }

            foreach (SuperPosition constrainedPos in constrainedSuperPositions)
            {
                if (!Propegate(constrainedPos))
                {
                    m_Unsolvable = true;
                    return;
                }
            }
        }

        // The socket for a side without a neighbour: the one the graph asks for there (a mesh border), otherwise the
        // default socket given to the constructor, otherwise null for a free side
        private string RequiredBorderSocket(Node node, NeighbourDirection direction)
        {
            return node.m_BoundarySockets[(int)direction] ?? m_DefaultSocket;
        }

        private static Node NeighbourNode(Node node, NeighbourDirection direction)
        {
            switch (direction)
            {
                case NeighbourDirection.POSITIVE_X: return node.m_xPos;
                case NeighbourDirection.NEGATIVE_X: return node.m_xNeg;
                case NeighbourDirection.POSITIVE_Z: return node.m_zPos;
                case NeighbourDirection.NEGATIVE_Z: return node.m_zNeg;
            }

            throw new Exception("Invalid neighbour direction");
        }

        // The position to collapse next: one of those with the fewest prototypes left, picked at random. Null when every
        // position is collapsed. Scans the open positions in m_SuperPositions order, dropping the collapsed ones as it
        // goes, so the candidates and the random pick are the same as scanning every position.
        private SuperPosition FindMinEntropyPosition()
        {
            if (m_Open == null) m_Open = new List<SuperPosition>(m_SuperPositions);

            int minEntropy = int.MaxValue;
            m_MinEntropyCandidates.Clear();

            int write = 0;
            for (int read = 0; read < m_Open.Count; read++)
            {
                SuperPosition superPos = m_Open[read];
                int entropy = superPos.m_PrototypeIndices.Count;
                if (entropy == 1) continue;

                m_Open[write++] = superPos;

                if (entropy < minEntropy)
                {
                    minEntropy = entropy;
                    m_MinEntropyCandidates.Clear();
                }

                if (entropy == minEntropy) m_MinEntropyCandidates.Add(superPos);
            }
            m_Open.RemoveRange(write, m_Open.Count - write);

            if (m_MinEntropyCandidates.Count == 0) return null;
            return m_MinEntropyCandidates[RandomSource.Next(m_MinEntropyCandidates.Count)];
        }

        private SuperPosition GetMinEntropyPosition()
        {
            return FindMinEntropyPosition() ?? throw new Exception("Already collapsed");
        }

        // The direction in which from sees to. On a mesh graph this isn't simply the opposite of to's direction to from.
        private static NeighbourDirection DirectionTowards(SuperPosition from, SuperPosition to)
        {
            if (from.GetNeighbour(NeighbourDirection.POSITIVE_X) == to) return NeighbourDirection.POSITIVE_X;
            if (from.GetNeighbour(NeighbourDirection.NEGATIVE_X) == to) return NeighbourDirection.NEGATIVE_X;
            if (from.GetNeighbour(NeighbourDirection.POSITIVE_Z) == to) return NeighbourDirection.POSITIVE_Z;
            if (from.GetNeighbour(NeighbourDirection.NEGATIVE_Z) == to) return NeighbourDirection.NEGATIVE_Z;
            throw new Exception("Could not find other direction");
        }

        // Describes what a tile at the position would need to fit its neighbours, see LastContradiction.
        // A prototype fits next to a neighbour when its socket towards it equals the neighbour's socket back, reversed.
        private string DescribeContradiction(SuperPosition position)
        {
            var text = new StringBuilder();
            string node = position.m_Node != null ? position.m_Node.m_ID.ToString() : "?";
            text.Append("No prototype fits node ").Append(node).AppendLine(". A tile there would need these sockets:");

            foreach (NeighbourDirection direction in Enum.GetValues(typeof(NeighbourDirection)))
            {
                text.Append("  ").Append(direction).Append(": ");

                SuperPosition neighbour = position.GetNeighbour(direction);
                if (neighbour == null)
                {
                    string border = position.m_Node != null ? RequiredBorderSocket(position.m_Node, direction) : null;
                    text.AppendLine(border != null ? "\"" + border + "\"  (required on this border side)" : "any (no neighbour)");
                    continue;
                }

                NeighbourDirection back = DirectionTowards(neighbour, position);
                var sockets = new SortedSet<string>(neighbour.m_PrototypeIndices.Select(i => m_Prototypes[i].sockets.GetSocketInDirection(back, true)));
                text.Append(string.Join(" or ", sockets.Select(s => "\"" + s + "\"")));

                if (neighbour.m_PrototypeIndices.Count == 1)
                {
                    Prototype p = m_Prototypes[neighbour.m_PrototypeIndices[0]];
                    text.Append("  (neighbour is ").Append(p.mesh_name).Append(" rotated ").Append(p.rotation).AppendLine(")");
                }
                else
                {
                    text.Append("  (neighbour can still be ").Append(neighbour.m_PrototypeIndices.Count).AppendLine(" prototypes)");
                }
            }

            return text.ToString();
        }

        // Fills m_NeighbourLookup from the prototypes
        private void BuildNeighbourLookup()
        {
            var lookup = new Dictionary<(string, NeighbourDirection), List<int>>();
            int maxId = m_Prototypes.Length - 1;
            foreach (Prototype prototype in m_Prototypes)
            {
                maxId = Math.Max(maxId, prototype.id);
                foreach (NeighbourDirection direction in Enum.GetValues(typeof(NeighbourDirection)))
                {
                    var key = (prototype.sockets.GetSocketInDirection(direction, true), direction);
                    if (!lookup.TryGetValue(key, out List<int> ids)) lookup[key] = ids = new List<int>();
                    ids.Add(prototype.id);
                }
            }

            m_NeighbourLookup = lookup.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            m_Allowed = new bool[maxId + 1];
        }

        // Sets m_Allowed for every prototype that fits next to some prototype of the position, on the neighbour in
        // direction: its socket facing back, reversed, equals that prototype's socket towards it. Clear it again with
        // ClearPossibleNeighbours.
        private void MarkPossibleNeighbours(SuperPosition superPosition, NeighbourDirection direction)
        {
            using (s_GetNeighboursPerfMarker.Auto())
            {
                //Find out which socket is facing me
                SuperPosition neighbour = superPosition.GetNeighbour(direction);
                NeighbourDirection otherDirection = DirectionTowards(neighbour, superPosition);

                m_SeenSockets.Clear();
                for (int i = 0; i < superPosition.m_PrototypeIndices.Count; i++)
                {
                    string socket = m_Prototypes[superPosition.m_PrototypeIndices[i]].sockets.GetSocketInDirection(direction);
                    if (!m_SeenSockets.Add(socket)) continue;
                    if (!m_NeighbourLookup.TryGetValue((socket, otherDirection), out int[] fits)) continue;

                    foreach (int id in fits)
                    {
                        if (m_Allowed[id]) continue;
                        m_Allowed[id] = true;
                        m_AllowedIds.Add(id);
                    }
                }
            }
        }

        private void ClearPossibleNeighbours()
        {
            foreach (int id in m_AllowedIds) m_Allowed[id] = false;
            m_AllowedIds.Clear();
        }

        private bool IsAllowed(int id) => id >= 0 && id < m_Allowed.Length && m_Allowed[id];

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
            m_Open = null;
        }

        public void UpdatePosition(SuperPosition superPosition, int prototype)
        {
            superPosition.m_PrototypeIndices = new List<int> {prototype};

            RecalculateNeighbourPrototypes(superPosition);
            m_Open = null;
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

        // Picks one of the given prototype indices at random, weighted by prototype weight. Useful in a custom
        // collapse after narrowing down a superposition's candidates.
        public int PickWeightedPrototype(List<int> prototypes)
        {
            //Find total weight sum
            float weightSum = 0;
            for (int i = 0; i < prototypes.Count; i++)
            {
                weightSum += m_Prototypes[prototypes[i]].weight;
            }
        
            if (weightSum == 0)
            {
                int uniformDraw = RandomSource.Next(prototypes.Count);
                return prototypes[uniformDraw];
            }
        
            //Make draw and reset sum
            float draw = (float)(RandomSource.NextDouble() * weightSum);
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
            MarkPossibleNeighbours(superPosition, direction);
            List<int> validNeighbourPrototypes = m_AllowedIds.OrderBy(id => id).ToList();
            List<int> intersection = new List<int>();

            foreach (var p in neighbour.m_PrototypeIndices)
            {
                if (IsAllowed(p))
                {
                    intersection.Add(p);
                }
            }
            ClearPossibleNeighbours();

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

            //Mark the possible prototypes in direction
            MarkPossibleNeighbours(superPosition, neighbourDirection);
            try
            {
                List<int> prototypes = neighbour.m_PrototypeIndices;

                int kept = 0;
                foreach (int id in prototypes)
                {
                    if (IsAllowed(id)) kept++;
                }

                //Every prototype still fits: nothing changes
                if (kept == prototypes.Count) return (true, null);

                //Nothing fits any more: a contradiction
                if (kept == 0)
                {
                    Debug.Log("Banning in a collapsedPosition");
                    LastContradiction = DescribeContradiction(neighbour);
                    return (false, null);
                }

                //Remove the prototypes that no longer fit, keeping the order of the rest
                int write = 0;
                for (int read = 0; read < prototypes.Count; read++)
                {
                    if (IsAllowed(prototypes[read])) prototypes[write++] = prototypes[read];
                }
                prototypes.RemoveRange(write, prototypes.Count - write);

                return (true, neighbour);
            }
            finally
            {
                ClearPossibleNeighbours();
            }
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
            return CollapseAndPropagate(GetMinEntropyPosition());
        }

        // Collapses the superposition, with the custom collapse if set, then propagates. Returns false on a contradiction.
        public bool CollapseAndPropagate(SuperPosition superPosition)
        {
            Collapse(superPosition);
            return Propegate(superPosition);
        }

        //Run the wave function collapse and store the result in the original connection graph
        public bool Solve()
        {
            if (m_Unsolvable) return false;

            for (SuperPosition next = FindMinEntropyPosition(); next != null; next = FindMinEntropyPosition())
            {
                if (!CollapseAndPropagate(next)) return false;
            }

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