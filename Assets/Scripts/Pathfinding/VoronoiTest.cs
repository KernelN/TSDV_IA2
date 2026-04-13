using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IA.Pathfinding.Voronoi
{
    public class VoronoiTest : MonoBehaviour
    {
        [System.Serializable]
        public class Point { public Vector2 p; }

        [System.Serializable]
        public class VoronoiRegion
        {
            public int      id;
            public Vector2  seedPos;
            public Color    color;
            public List<int> adjacentIds = new List<int>();
            [System.NonSerialized] public HashSet<int> adjacentRegionIds = new HashSet<int>();

            public VoronoiRegion(int id, Vector2 seedPos, Color color)
            {
                this.id = id; this.seedPos = seedPos; this.color = color;
            }
        }

        [Header("Set Values")]
        [SerializeField] Vector2Int points;
        [SerializeField] Point[]    pointsOfInterest;
        [SerializeField] RawImage   img;
        [SerializeField] Color[]    colors;
        [SerializeField] bool showBoundaries = true;
        [SerializeField] bool showSeeds      = true;
        [SerializeField] int  seedDotRadius  = 3;

        [Header("Runtime Values")]
        [SerializeField] Vector2         imgSize;
        [SerializeField] VoronoiRegion[] regions;

        int[,]                           regionMap;
        List<(Vector2 from, Vector2 to)> voronoiEdgeLines = new List<(Vector2, Vector2)>();

        // -----------------------------------------------------------------------
        // Unity calls Start() once when the scene begins playing.
        // We use it as our entry point to run the full generation pipeline.
        // -----------------------------------------------------------------------
        void Start()
        {
            imgSize = img.rectTransform.sizeDelta;

            InitializeRegions();

            Vector2[] seeds = new Vector2[regions.Length];
            for (int i = 0; i < regions.Length; i++) seeds[i] = regions[i].seedPos;

            BowyerWatsonResult result = Voronoi.BuildBowyerWatsonVoronoi(seeds, points);
            regionMap        = result.regionMap;
            voronoiEdgeLines = result.edgeLines;

            for (int i = 0; i < regions.Length; i++)
            {
                regions[i].adjacentRegionIds = new HashSet<int>(result.adjacencyByRegion[i]);
                regions[i].adjacentIds       = new List<int>(result.adjacencyByRegion[i]);
                regions[i].adjacentIds.Sort();
            }

            RenderDiagram();
        }

        // -----------------------------------------------------------------------
        // InitializeRegions
        // Purpose : Convert the inspector-defined normalized (0–1) seed positions
        //           into actual grid-pixel coordinates and wrap them in VoronoiRegion objects.
        // -----------------------------------------------------------------------
        void InitializeRegions()
        {
            regions = new VoronoiRegion[pointsOfInterest.Length];

            for (int i = 0; i < pointsOfInterest.Length; i++)
            {
                Color c = i < colors.Length ? colors[i] : colors[Random.Range(0, colors.Length)];

                float x = Mathf.Clamp(pointsOfInterest[i].p.x * points.x, 0f, points.x - 1f);
                float y = Mathf.Clamp(pointsOfInterest[i].p.y * points.y, 0f, points.y - 1f);

                regions[i] = new VoronoiRegion(i, new Vector2(x, y), c);
            }
        }

        // -----------------------------------------------------------------------
        // RenderDiagram
        // Purpose : Produce the final texture and display it on the RawImage.
        //           a) Paint colors   — write region colors to the texture
        //           b) Draw edges     — rasterize Voronoi edge lines with Bresenham
        //           c) Draw seeds     — mark seed positions with dots
        // -----------------------------------------------------------------------
        void RenderDiagram()
        {
            int texW = (int)imgSize.x, texH = (int)imgSize.y;

            Texture2D tex = new Texture2D(texW, texH);
            tex.filterMode = FilterMode.Point;

            // ── a) Paint region colors onto the texture ──────────────────────────
            for (int x = 0; x < points.x; x++)
            {
                int texX0 = Mathf.RoundToInt((float)x       / points.x * texW);
                int texX1 = Mathf.RoundToInt((float)(x + 1) / points.x * texW);

                for (int y = 0; y < points.y; y++)
                {
                    Color c = regionMap[x, y] >= 0 ? regions[regionMap[x, y]].color : Color.black;

                    int texY0 = Mathf.RoundToInt((float)y       / points.y * texH);
                    int texY1 = Mathf.RoundToInt((float)(y + 1) / points.y * texH);

                    for (int px = texX0; px < texX1; px++)
                        for (int py = texY0; py < texY1; py++)
                            tex.SetPixel(px, py, c);
                }
            }

            // ── b) Draw Voronoi edge lines using Bresenham's algorithm ───────────
            if (showBoundaries && voronoiEdgeLines != null)
            {
                foreach ((Vector2 from, Vector2 to) in voronoiEdgeLines)
                    DrawLine(tex, GridToTex(from, texW, texH), GridToTex(to, texW, texH), Color.black);
            }

            // ── c) Draw seed position markers ────────────────────────────────────
            if (showSeeds)
            {
                foreach (VoronoiRegion region in regions)
                {
                    Vector2Int tp = GridToTex(region.seedPos, texW, texH);
                    DrawDot(tex, tp.x, tp.y, seedDotRadius, Color.white);
                    DrawDot(tex, tp.x, tp.y, Mathf.Max(1, seedDotRadius - 1), Color.black);
                }
            }

            tex.Apply();
            img.texture = tex;
        }

        // -----------------------------------------------------------------------
        // GridToTex
        // Purpose  : Convert a position in grid space to the closest integer pixel
        //            coordinate in texture space.
        // -----------------------------------------------------------------------
        Vector2Int GridToTex(Vector2 gridPos, int texW, int texH) => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(gridPos.x / points.x * texW), 0, texW - 1),
            Mathf.Clamp(Mathf.RoundToInt(gridPos.y / points.y * texH), 0, texH - 1));

        // -----------------------------------------------------------------------
        // DrawLine  (Bresenham's line algorithm)
        // -----------------------------------------------------------------------
        void DrawLine(Texture2D tex, Vector2Int p0, Vector2Int p1, Color c)
        {
            int x0 = p0.x, y0 = p0.y, x1 = p1.x, y1 = p1.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < tex.width && y0 >= 0 && y0 < tex.height)
                    tex.SetPixel(x0, y0, c);

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 <  dx) { err += dx; y0 += sy; }
            }
        }

        // -----------------------------------------------------------------------
        // DrawDot
        // Purpose  : Paint a filled circle of a given radius onto the texture.
        // -----------------------------------------------------------------------
        void DrawDot(Texture2D tex, int cx, int cy, int radius, Color c)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (dx * dx + dy * dy <= radius * radius)
                        tex.SetPixel(
                            Mathf.Clamp(cx + dx, 0, tex.width  - 1),
                            Mathf.Clamp(cy + dy, 0, tex.height - 1), c);
        }

        // -----------------------------------------------------------------------
        // FindPathThroughRegions  (A* Voronoi — groundwork stub)
        // -----------------------------------------------------------------------
        public List<int> FindPathThroughRegions(int startRegionId, int goalRegionId)
        {
            throw new System.NotImplementedException(
                "A* Voronoi pathfinding will be implemented in the next phase.");
        }
    }
}
