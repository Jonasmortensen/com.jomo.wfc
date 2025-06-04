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

            public List<Vector3> GetRotatedPoints()
            {
                Vector3[] temp = {p3, p0, p1, p2};

                int rotation = m_Prototype.rotation;

                return new List<Vector3> { temp[(0+rotation)%4], temp[(1+rotation)%4], temp[(2+rotation)%4], temp[(3+rotation)%4] };

            }
        }
    
        
        public static TileConnectionGraph FromMesh(HalfEdgeMesh.Mesh mesh)
        {

            Dictionary<int, Node> nodes = new Dictionary<int, Node>();

            //Create nodes
            int faceId = 0;
            foreach (var face in mesh.Faces)
            {
                var verts = face.GetVertices();
                if (verts.Count != 4) throw new Exception("I can only do WFC on quad meshes");

                var node = new MeshNode();
                node.m_ID = faceId;
                face.id = faceId;

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
                var node = nodes[face.id] as MeshNode;

                var edge = face.Edge;
                var neighbour = edge.Twin.IncidentFace;

                if (neighbour != null)
                {
                    node.m_xPos = nodes[neighbour.id];

                }

                edge = edge.Next;
                neighbour = edge.Twin.IncidentFace;

                if (neighbour != null)
                {
                    node.m_zNeg = nodes[neighbour.id];
                }

                //node.p2 = edge.Next.Origin.Position;
                //node.p3 = edge.Origin.Position;

                edge = edge.Next;
                neighbour = edge.Twin.IncidentFace;

                if (neighbour != null)
                {
                    node.m_xNeg = nodes[neighbour.id];
                }

                edge = edge.Next;
                neighbour = edge.Twin.IncidentFace;

                if (neighbour != null)
                {
                    node.m_zPos = nodes[neighbour.id];
                }
                //node.p0 = edge.Origin.Position;
                //node.p1 = edge.Next.Origin.Position;


            }

            TileConnectionGraph graph = new TileConnectionGraph();
            graph.nodes = nodes.Values.ToList();

            return graph;
        }
        
    
    
    }
}