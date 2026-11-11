using UnityEngine;
using System.Collections;

public class CombineMeshes : MonoBehaviour
{
    public bool regenerateMesh = true;

    void UpdateMesh()
    {
        // save current position so we can set it to zero so the localToWorldMatrix works correctly
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];
        int i = 0;
        while (i < meshFilters.Length)
        {
            combine[i].mesh = meshFilters[i].sharedMesh;
            combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
            meshFilters[i].gameObject.transform.GetComponent<Renderer>().enabled = false;
            i++;
        }

        MeshFilter meshFilter = transform.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = transform.gameObject.AddComponent<MeshFilter>();
        }
        Renderer renderer = transform.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = transform.gameObject.AddComponent<Renderer>();
        }

        meshFilter.mesh = new Mesh();
        meshFilter.mesh.CombineMeshes(combine);
        meshFilter.mesh.Optimize();

        //transform.gameObject.SetActive(true);

        //restore position
        transform.position = position;
        transform.rotation = rotation;

        regenerateMesh = false;
    }

    // Use this for initialization
    void Start ()
    {
        UpdateMesh();
    }

    void Update()
    {
        if (regenerateMesh == true)
        {
            UpdateMesh();
        }
    }

}
