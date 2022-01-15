using System.Collections.Generic;
using UnityEngine;
using PathCreation;
using System.Linq;

public class VinePoint {
    public Vector3 origin, normal, pointAtNormal;
    public VinePoint(Vector3 origin, Vector3 normal, float thickness) {
        this.origin = origin + normal * thickness;
        this.normal = normal;
        this.pointAtNormal = origin + normal;
    }
}

public class VineBranch {
    VineTree tree;

    int maxPointCount = 20;
    int maxDirectionTryCount = 40;
    private float rayCastStep = 0.2f;
    List<VinePoint> points = new List<VinePoint>();

    VertexPath vertexPath;
    Mesh mesh;
    GameObject meshHolder;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    public float textureTiling = 1;

    public VineBranch(VineTree tree) {
        this.tree = tree;

        points.Add(CreateVinePoint(tree.origin, tree.normal));
    }

    private VinePoint CreateVinePoint(Vector3 origin, Vector3 normal) {
        return new VinePoint(origin, normal, tree.planter.branchThickness);
    }

    private VinePoint CreateVinePoint(RaycastHit hit)
    {
        return CreateVinePoint(hit.point, hit.normal);
    }

    public void Grow() {
        VinePoint point;
        
        while (points.Count < maxPointCount && (point = FindNewPoint()) != null) {
            points.Add(point);
        }

        CreatePath();
    }

    public void CreatePath()
    {
        AssignMeshComponents();
        AssignMaterials();
        CreateVertexPath();
        CreateRoadMesh();
        CreateLeaves();
    }

    // This leaves (get it?) a lot to be desired
    //
    private void CreateLeaves() {
        if (vertexPath == null) {
            return;
        }

        float step = tree.LeafAmount;
        for (float i = 0; i < 1 - step; i += step)
        {
            // if (i >= 1 - step) continue;

            Vector3 point = vertexPath.GetPointAtTime(i);
            Vector3 normal = vertexPath.GetNormal(i);
            Vector3 nextPoint = vertexPath.GetPointAtTime(i + step);

            Vector3 direction = nextPoint - point;
            Vector3 cross = Vector3.Cross(direction, normal);
            
            Vector3 randomWidthOffset = cross * Random.Range(-0.5f, 0.5f);
            Vector3 randomHeightOffset = normal * Random.Range(0.2f, 0.2f);

            if (normal.x == double.NaN) continue;

            GameObject instance = GameObject.Instantiate(tree.LeafPrefab, point + randomHeightOffset + randomWidthOffset / 2, Quaternion.FromToRotation(Vector3.up, normal));

            if (nextPoint != Vector3.zero) {
                instance.transform.LookAt(nextPoint + randomHeightOffset + normal * 0.2f + randomWidthOffset, normal);
            }
        }
    }

    private void CreateVertexPath() {
        if (points.Count < 2) {
            vertexPath = null;
            return;
        }

        BezierPath bezierPath = new BezierPath(points.Select(point => point.origin).ToArray(), false, PathSpace.xyz);
        for (int i = 0; i < bezierPath.NumAnchorPoints; i++)
        {
            bezierPath.Normals.Add(points[i].normal);
        }

        vertexPath = new VertexPath(bezierPath, meshHolder.transform);
    }

    private Vector3 GetNewDirection(VinePoint p1, VinePoint p2) {
        if (p1 == null) {
            return Vector3.ProjectOnPlane(Random.insideUnitSphere, p2.normal).normalized;
        }

        Vector3 direction = Vector3.ProjectOnPlane(p2.origin - p1.origin, p2.normal);
        Vector3 randomDirection = Vector3.ProjectOnPlane(Random.insideUnitSphere, p2.normal);
        int tries = 0;
        while (tries < maxDirectionTryCount && Vector3.Dot(direction, randomDirection) < 0.5) {
            randomDirection = Vector3.ProjectOnPlane(Random.insideUnitSphere, p2.normal);
            tries += 1;
        }

        return randomDirection;
    }

    public VinePoint FindNewPoint() {
        VinePoint pointBeforeLast = null;
        if (points.Count >= 2) {
            pointBeforeLast = points[points.Count - 2];
        }
        VinePoint lastPoint = points[points.Count - 1];

        Vector3 direction = GetNewDirection(pointBeforeLast, lastPoint);

        RaycastHit hit;

        // the branch hit a wall
        if (Physics.Raycast(lastPoint.pointAtNormal, direction, out hit, 2f)) {
            return CreateVinePoint(hit);
        }

        RaycastHit lastSuccessfulHit = new RaycastHit();
        bool didHit = false;
        Vector3 rayCastDirection = direction;
        float distance = rayCastStep;
        while (distance <= 2 && Physics.Raycast(lastPoint.pointAtNormal + rayCastDirection * distance, -lastPoint.normal, out hit, 2f)) {
            didHit = true;
            lastSuccessfulHit = hit;
            distance += rayCastStep;
        }

        if (didHit) {
            return CreateVinePoint(lastSuccessfulHit);
        }

        Vector3 rayCastPoint = lastPoint.pointAtNormal + direction - 2 * lastPoint.normal;
        if (Physics.Raycast(rayCastPoint, -direction, out hit, 2f))
        {
            return CreateVinePoint(hit);
        }

        Debug.Log("OOPS");
        return null;
    }

    public void Redraw() {
        CreateVertexPath();
        CreateRoadMesh();
    }

    void CreateRoadMesh() {
        if (vertexPath == null) return;

        bool flattenSurface = false;

        Vector3[] verts = new Vector3[vertexPath.NumPoints * 8];
        Vector2[] uvs = new Vector2[verts.Length];
        Vector3[] normals = new Vector3[verts.Length];

        int numTris = 2 * (vertexPath.NumPoints - 1) + ((vertexPath.isClosedLoop) ? 2 : 0);
        int[] roadTriangles = new int[numTris * 3];
        int[] underRoadTriangles = new int[numTris * 3];
        int[] sideOfRoadTriangles = new int[numTris * 2 * 3];

        int vertIndex = 0;
        int triIndex = 0;

        // Vertices for the top of the road are layed out:
        // 0  1
        // 8  9
        // and so on... So the triangle map 0,8,1 for example, defines a triangle from top left to bottom left to bottom right.
        int[] triangleMap = { 0, 8, 1, 1, 8, 9 };
        int[] sidesTriangleMap = { 4, 6, 14, 12, 4, 14, 5, 15, 7, 13, 15, 5 };

        bool usePathNormals = !(vertexPath.space == PathSpace.xyz && flattenSurface);

        for (int i = 0; i < vertexPath.NumPoints; i++)
        {
            Vector3 localUp = (usePathNormals) ? Vector3.Cross(vertexPath.GetTangent(i), vertexPath.GetNormal(i)) : vertexPath.up;
            Vector3 localRight = (usePathNormals) ? vertexPath.GetNormal(i) : Vector3.Cross(localUp, vertexPath.GetTangent(i));

            // Find position to left and right of current path vertex
            Vector3 vertSideA = vertexPath.GetPoint(i) - localRight * Mathf.Abs(tree.branchWidth);
            Vector3 vertSideB = vertexPath.GetPoint(i) + localRight * Mathf.Abs(tree.branchWidth);

            // Add top of road vertices
            verts[vertIndex + 0] = vertSideA;
            verts[vertIndex + 1] = vertSideB;
            // Add bottom of road vertices
            verts[vertIndex + 2] = vertSideA - localUp * tree.branchThickness;
            verts[vertIndex + 3] = vertSideB - localUp * tree.branchThickness;

            // Duplicate vertices to get flat shading for sides of road
            verts[vertIndex + 4] = verts[vertIndex + 0];
            verts[vertIndex + 5] = verts[vertIndex + 1];
            verts[vertIndex + 6] = verts[vertIndex + 2];
            verts[vertIndex + 7] = verts[vertIndex + 3];

            // Set uv on y axis to path time (0 at start of path, up to 1 at end of path)
            uvs[vertIndex + 0] = new Vector2(0, vertexPath.times[i]);
            uvs[vertIndex + 1] = new Vector2(1, vertexPath.times[i]);

            // Top of road normals
            normals[vertIndex + 0] = localUp;
            normals[vertIndex + 1] = localUp;
            // Bottom of road normals
            normals[vertIndex + 2] = -localUp;
            normals[vertIndex + 3] = -localUp;
            // Sides of road normals
            normals[vertIndex + 4] = -localRight;
            normals[vertIndex + 5] = localRight;
            normals[vertIndex + 6] = -localRight;
            normals[vertIndex + 7] = localRight;

            // Set triangle indices
            if (i < vertexPath.NumPoints - 1 || vertexPath.isClosedLoop)
            {
                for (int j = 0; j < triangleMap.Length; j++)
                {
                    roadTriangles[triIndex + j] = (vertIndex + triangleMap[j]) % verts.Length;
                    // reverse triangle map for under road so that triangles wind the other way and are visible from underneath
                    underRoadTriangles[triIndex + j] = (vertIndex + triangleMap[triangleMap.Length - 1 - j] + 2) % verts.Length;
                }
                for (int j = 0; j < sidesTriangleMap.Length; j++)
                {
                    sideOfRoadTriangles[triIndex * 2 + j] = (vertIndex + sidesTriangleMap[j]) % verts.Length;
                }

            }

            vertIndex += 8;
            triIndex += 6;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.subMeshCount = 3;
        mesh.SetTriangles(roadTriangles, 0);
        mesh.SetTriangles(underRoadTriangles, 1);
        mesh.SetTriangles(sideOfRoadTriangles, 2);
        mesh.RecalculateBounds();
    }

    void AssignMeshComponents()
    {

        if (meshHolder == null)
        {
            meshHolder = new GameObject("Road Mesh Holder");
        }

        meshHolder.transform.rotation = Quaternion.identity;
        meshHolder.transform.position = Vector3.zero;
        meshHolder.transform.localScale = Vector3.one;

        // Ensure mesh renderer and filter components are assigned
        if (!meshHolder.gameObject.GetComponent<MeshFilter>())
        {
            meshHolder.gameObject.AddComponent<MeshFilter>();
        }
        if (!meshHolder.GetComponent<MeshRenderer>())
        {
            meshHolder.gameObject.AddComponent<MeshRenderer>();
        }

        meshRenderer = meshHolder.GetComponent<MeshRenderer>();
        meshFilter = meshHolder.GetComponent<MeshFilter>();
        if (mesh == null)
        {
            mesh = new Mesh();
        }
        meshFilter.sharedMesh = mesh;
    }

    void AssignMaterials()
    {
        if (tree.planter.vineTopMat != null && tree.planter.vineBottomMat != null)
        {
            meshRenderer.sharedMaterials = new Material[] { tree.planter.vineTopMat, tree.planter.vineBottomMat, tree.planter.vineBottomMat };
            meshRenderer.sharedMaterials[0].mainTextureScale = new Vector3(1, textureTiling);
        }
    }

    public void DrawGizmos()
    {
        // DrawDebugGizmos();
        DrawVertexPathNormalsGizmos();
        // DrawVertexPathGizmos();
    }

    private void DrawVertexPathGizmos()
    {
        if (vertexPath == null) return;

        for (int i = 0; i < vertexPath.NumPoints; i++)
        {
            int nextI = i + 1;
            if (nextI >= vertexPath.NumPoints)
            {
                if (vertexPath.isClosedLoop)
                {
                    nextI %= vertexPath.NumPoints;
                }
                else
                {
                    break;
                }
            }

            Gizmos.DrawLine(vertexPath.GetPoint(i), vertexPath.GetPoint(nextI));
        }
    }

    private void DrawDebugGizmos() {
        for (int i = 0; i < points.Count; i++)
        {
            VinePoint point = points[i];
            VinePoint nextPoint = null;
            if (i + 1 < points.Count)
            {
                nextPoint = points[i + 1];
            }

            Gizmos.color = i == 0 ? Color.black : Color.green;
            Gizmos.DrawSphere(point.origin, 0.1f);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(point.origin, point.origin + point.normal * 0.2f);

            if (nextPoint != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(point.origin + point.normal * 0.2f, nextPoint.origin + nextPoint.normal * 0.2f);
            }
        }
    }

    private void DrawVertexPathNormalsGizmos()
    {
        // for (int i = 0; i < vertexPath.localNormals.Length; i++)
        // {
        //     Gizmos.color = Color.white;
        //     Gizmos.DrawLine(vertexPath.localPoints[i], vertexPath.localPoints[i] + vertexPath.localNormals[i]);
        //     Gizmos.color = Color.blue;
        //     Gizmos.DrawLine(vertexPath.localPoints[i], vertexPath.localPoints[i] + vertexPath.localNormals[i]);
        // }

        float step = tree.LeafAmount;
        for (float i = 0; i < 1; i += step)
        {
            Vector3 point = vertexPath.GetPointAtTime(i);
            Vector3 normal = vertexPath.GetNormal(i);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(point, point + normal);
            Gizmos.color = Color.black;
        }
    }
}