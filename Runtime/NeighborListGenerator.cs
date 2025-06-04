using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Random = UnityEngine.Random;


namespace Jomo.WFC
{
    //This class builds a list of prototypes with neighbour lists
    public class NeighborListGenerator : MonoBehaviour
    {
        public TextAsset jsonFile;
        public GameObject m_Geometry;
        public Prototype[] m_Prototypes;
        public float m_TileSize;
        public bool m_AddRotations;
        public float m_Radius;
        public int m_Subdivisions;
        public int m_RelaxCount;
    
        //public HalfEdgeMeshDebugView m_DebugView;

        private Dictionary<string, GameObject> m_GeometryMap;

        public bool button;

        private WFCSolver solver;
    
        // Start is called before the first frame update
        void Start()
        {
            BuildGeometryDictionary();
        
            m_Prototypes = Prototype.PrototypesFromJsom(jsonFile.text, m_AddRotations);

            //DoSolve();
        }


        /*
        //Should not be here
        public void DoSolve()
        {
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            Profiler.BeginSample("Build Mesh");
            var mesh = GridGeneratorv3.CreateGridd(m_Radius, m_Subdivisions, m_RelaxCount, Random.Range(0, Int32.MaxValue));
            //if (m_DebugView != null) m_DebugView.m_Mesh = mesh;
            //TileConnectionGraph tileConnectionGraph = TileConnectionGraph.FromMesh(mesh);
            Profiler.EndSample();


            //TileConnectionGraph tileConnectionGraph = TileConnectionGraph.Grid(10, 10);

            //solver = new WFCSolver(m_Prototypes, tileConnectionGraph);

            //Solve should probably return the graph?
            solver.Solve();

            Debug.Log("Done solving!");
            //MeshInstantiate(tileConnectionGraph);
            //InstantiateGridCollapse(tileConnectionGraph);
        }
        */
    
        private void BuildGeometryDictionary()
        {
            m_GeometryMap = new Dictionary<string, GameObject>();

            foreach (Transform child in m_Geometry.transform)
            {
                m_GeometryMap.Add(child.name, child.gameObject);
            }
        }

        /*
        // Update is called once per frame
        void Update()
        {
            if (button)
            {
                DoSolve();
                button = false;
            }
        }
        */

        private void MeshInstantiate(TileConnectionGraph tileConnectionGraph)
        {
            var someNode = tileConnectionGraph.nodes[10] as TileConnectionGraph.MeshNode;
        
            float scale = Vector3.Distance(someNode.p0, someNode.p1);
        
        
            foreach (var node in tileConnectionGraph.nodes)
            {
                Prototype tilePrototype = node.m_Prototype;

                if (tilePrototype == null) continue;
            
                if(tilePrototype.mesh_name == "-1") continue;
            
                TileConnectionGraph.MeshNode meshNode = node as TileConnectionGraph.MeshNode;
            
                GameObject tileGO = Instantiate(m_GeometryMap[tilePrototype.mesh_name]);
                tileGO.name = tilePrototype.id + "";
                tileGO.transform.parent = transform;
                tileGO.transform.localScale = Vector3.one * scale;
                tileGO.transform.position = meshNode.position;
            
                Vector3[] points = {meshNode.p0, meshNode.p1, meshNode.p3, meshNode.p2};

                var renderer = tileGO.GetComponent<MeshRenderer>();

                int i = tilePrototype.rotation;



                foreach (var material in renderer.materials)
                {
                    material.SetVector("_p1", points[i]);
                    material.SetVector("_p2", points[(i+1)%4]);
                    material.SetVector("_p4", points[(i+2)%4]);
                    material.SetVector("_p3", points[(i+3)%4]);
                }
            
                //This only does something for shading
                tileGO.transform.right = (meshNode.p1 - meshNode.p2).normalized;
                tileGO.transform.Rotate(Vector3.up, 90 * tilePrototype.rotation);
            }
        }

        private void InstantiateGridCollapse(TileConnectionGraph tileConnectionGraph)
        {
            foreach (var node in tileConnectionGraph.nodes)
            {
                Prototype tilePrototype = node.m_Prototype;

                if (tilePrototype == null) continue;
            
                if(tilePrototype.mesh_name == "-1") continue;
            
                TileConnectionGraph.GridNode gridNode = node as TileConnectionGraph.GridNode;
            
                GameObject tileGO = Instantiate(m_GeometryMap[tilePrototype.mesh_name]);
                tileGO.name = tilePrototype.id + "";
                tileGO.transform.parent = transform;
                tileGO.transform.localScale = Vector3.one;
                tileGO.transform.localPosition = new Vector3(gridNode.m_Position.x, 0, gridNode.m_Position.y) * m_TileSize;
                tileGO.transform.Rotate(Vector3.up, 90 * tilePrototype.rotation);
            }
        }
    }

}