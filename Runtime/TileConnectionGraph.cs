using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace Jomo.WFC
{
    public class Node
    {
        public int m_ID;
        public Node m_xPos;
        public Node m_xNeg;
        public Node m_zPos;
        public Node m_zNeg;

        // Sockets required on sides without a neighbour, indexed by NeighbourDirection. null leaves a side free.
        // Used to make a mesh's border match whatever lies beyond it.
        public string[] m_BoundarySockets = new string[4];
        
        //used once the result is done
        public Prototype m_Prototype;
    }

    public class TileConnectionGraph
    {
        public List<Node> nodes = new List<Node>();
    
        public class GridNode : Node
        {
            public Vector2Int m_Position;
        }
        public static TileConnectionGraph Grid(int width, int height)
        {
            TileConnectionGraph graph = new TileConnectionGraph();
            Node[,] localNodes = new Node[width, height];
            graph.nodes = new List<Node>();

            int id = 0;

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    GridNode node = new GridNode();
                    node.m_Position = new Vector2Int(i, j);
                
                    node.m_ID = id;
                    id++;
                
                    localNodes[i, j] = node;
                    graph.nodes.Add(node);
                }
            }

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Node node = localNodes[i, j];
                    if(i+1 < width)
                        node.m_xPos = localNodes[i+1, j];
                    if(i-1 >=0) 
                        node.m_xNeg = localNodes[i-1, j];
                    if(j+1 < height) 
                        node.m_zPos = localNodes[i, j+1];
                    if(j-1 >= 0) 
                        node.m_zNeg = localNodes[i, j-1];
                }
            }

            return graph;
        }

    
        public static TileConnectionGraph HexTest()
        {
            TileConnectionGraph graph = new TileConnectionGraph();
        
            graph.nodes = new List<Node>();
        
            Node n0 = new Node();
            n0.m_ID = 0;
            Node n1 = new Node();
            n1.m_ID = 1;
            Node n2 = new Node();
            n2.m_ID = 2;

            n0.m_xPos = n1;
            n0.m_zNeg = n2;

            n1.m_xNeg = n2;
            n1.m_zPos = n0;

            n2.m_xNeg = n1;
            n2.m_zNeg = n0;
        
            graph.nodes.Add(n0);
            graph.nodes.Add(n1);
            graph.nodes.Add(n2);
        
            return graph;
        }


        public class MeshNode : Node
        {
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 p2;
            public Vector3 p3;

            public Vector3 position;

            // The face this node was built from, for reading its Data
            public Jomo.HalfEdgeMesh.Face m_Face;

            public List<Vector3> GetRotatedPoints()
            {
                Vector3[] temp = {p3, p0, p1, p2};

                int rotation = m_Prototype.rotation;

                return new List<Vector3> { temp[(0+rotation)%4], temp[(1+rotation)%4], temp[(2+rotation)%4], temp[(3+rotation)%4] };

            }
        }
    
        
        // The direction each of a face's four half-edges faces, in order from face.Edge. Faces are wound clockwise, like
        // the sides of a tile read clockwise, so a socket string runs from the start of its half-edge to the end.
        public static readonly NeighbourDirection[] EdgeDirections =
        {
            NeighbourDirection.POSITIVE_X, NeighbourDirection.NEGATIVE_Z, NeighbourDirection.NEGATIVE_X, NeighbourDirection.POSITIVE_Z,
        };

        // Fully qualified, since inside Jomo.WFC the name HalfEdgeMesh means the namespace Jomo.HalfEdgeMesh.
        // boundarySocket, if given, is asked for the socket each face's side on the mesh boundary must have, by the
        // face's half-edge on that side. Returning null leaves the side free.
        public static TileConnectionGraph FromMesh(Jomo.HalfEdgeMesh.HalfEdgeMesh mesh, Func<Jomo.HalfEdgeMesh.HalfEdge, string> boundarySocket = null)
        {

            Dictionary<int, Node> nodes = new Dictionary<int, Node>();
            var faceIds = new Dictionary<Jomo.HalfEdgeMesh.Face, int>();

            //Create nodes
            int faceId = 0;
            foreach (var face in mesh.Faces)
            {
                var verts = face.GetVertices();
                if (verts.Count != 4) throw new Exception("I can only do WFC on quad meshes");

                var node = new MeshNode();
                node.m_ID = faceId;
                node.m_Face = face;
                faceIds[face] = faceId;

                var avgPosition = (verts[0].Position + verts[1].Position + verts[2].Position + verts[3].Position)/4;
                node.position = avgPosition;
                node.p0 = verts[0].Position;
                node.p1 = verts[1].Position;
                node.p2 = verts[2].Position;
                node.p3 = verts[3].Position;

                nodes[faceId] = node;

                faceId++;
            }

            //Set up adjacency
            //TODO: Edges might not match in oppositions. Not sure if that is a problem
            foreach (var face in mesh.Faces)
            {
                var node = nodes[faceIds[face]] as MeshNode;

                var edge = face.Edge;
                foreach (NeighbourDirection direction in EdgeDirections)
                {
                    var neighbour = edge.Twin.IncidentFace;
                    if (neighbour != null)
                    {
                        Node other = nodes[faceIds[neighbour]];
                        switch (direction)
                        {
                            case NeighbourDirection.POSITIVE_X: node.m_xPos = other; break;
                            case NeighbourDirection.NEGATIVE_Z: node.m_zNeg = other; break;
                            case NeighbourDirection.NEGATIVE_X: node.m_xNeg = other; break;
                            case NeighbourDirection.POSITIVE_Z: node.m_zPos = other; break;
                        }
                    }
                    else if (boundarySocket != null)
                    {
                        node.m_BoundarySockets[(int)direction] = boundarySocket(edge);
                    }

                    edge = edge.Next;
                }
            }

            TileConnectionGraph graph = new TileConnectionGraph();
            graph.nodes = nodes.Values.ToList();

            return graph;
        }
        
    
    
    }
}