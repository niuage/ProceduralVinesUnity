using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VinePlanter : MonoBehaviour
{
    public GameObject[] leafPrefabs;

    public float leafAmount = 0.06f;

    public Material vineTopMat, vineBottomMat;

    public float branchThickness = 0.1f;
    public float branchWidth = 0.5f;

    public float angleNormal = 0f;

    List<VineTree> trees = new List<VineTree>();

    // Start is called before the first frame update
    void Start()
    {
        trees = new List<VineTree>();
    }

    private void Redraw() {
        for (int i = 0; i < trees.Count; i++)
        {
            trees[i].Redraw();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Redraw();
        }

        if (Input.GetMouseButtonDown(0)) {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                VineTree tree = new VineTree(origin: hit.point, normal: hit.normal, planter: this);
                trees.Add(tree);

                tree.Grow();
            }
        }
    }

    private void OnDrawGizmos() {
        for (int i = 0; i < trees.Count; i++) {
            trees[i].DrawGizmos();
        }
    }
}
