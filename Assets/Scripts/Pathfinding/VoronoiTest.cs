using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace IA.Pathfinding.Voronoi
{
    public class VoronoiTest : MonoBehaviour
    {
        [System.Serializable]
        public class Point { public Vector2 p; }

        class SitePaintData
        {
            public int id;
            public Vector2Int gridPos;
            public Color color;
        }

        [Header("Set Values")]
        [SerializeField] Vector2Int points = new Vector2Int(32, 32);
        [SerializeField] Point[] pointsOfInterest;
        [SerializeField] RawImage img;
        [SerializeField] Color[] colors;
        [SerializeField] Color unpaintedColor = Color.black;
        [SerializeField] bool showBoundaries = true;
        [SerializeField] bool showSeeds = true;
        [SerializeField] int seedDotRadius = 3;

        [Header("Debug Point")]
        [SerializeField] Transform debugPoint;
        [SerializeField] Transform debugWorldCenter;
        [SerializeField] Vector2 debugWorldSize = new Vector2(10f, 10f);

        [Header("Runtime Values")]
        [SerializeField] Vector2 imgSize;

        Vector2Int gridSize;
        Voronoi voronoi;
        PathNode[,] nodeGrid;
        PointOfInterest[] sites;
        Texture2D texture;
        Color[,] paintedCellColors;
        bool[,] hasPaintedCellColor;

        readonly Dictionary<int, SitePaintData> sitePaintDataById = new Dictionary<int, SitePaintData>();
        readonly Dictionary<Vector2Int, int> siteIdByGridPos = new Dictionary<Vector2Int, int>();

        bool lastShowBoundaries;
        bool lastShowSeeds;

        void Start()
        {
            InitializeTester();
            RenderDiagram();
        }

        void Update()
        {
            if (voronoi == null)
                return;

            bool needsRerender = false;

            if (showBoundaries != lastShowBoundaries || showSeeds != lastShowSeeds)
            {
                lastShowBoundaries = showBoundaries;
                lastShowSeeds = showSeeds;
                needsRerender = true;
            }

            if (TryGetDebugGridPosition(out Vector2Int debugGridPos))
            {
                Vector2Int closestSiteGridPos = voronoi.GetClosestSite(debugGridPos);
                if (siteIdByGridPos.TryGetValue(closestSiteGridPos, out int siteId) &&
                    sitePaintDataById.TryGetValue(siteId, out SitePaintData sitePaintData))
                {
                    if (!hasPaintedCellColor[debugGridPos.x, debugGridPos.y] ||
                        paintedCellColors[debugGridPos.x, debugGridPos.y] != sitePaintData.color)
                    {
                        paintedCellColors[debugGridPos.x, debugGridPos.y] = sitePaintData.color;
                        hasPaintedCellColor[debugGridPos.x, debugGridPos.y] = true;
                        needsRerender = true;
                    }
                }
            }

            if (needsRerender)
                RenderDiagram();
        }

        void OnDestroy()
        {
            if (texture != null)
                Destroy(texture);
        }

        void InitializeTester()
        {
            gridSize = new Vector2Int(Mathf.Max(1, points.x), Mathf.Max(1, points.y));
            imgSize = img != null ? img.rectTransform.rect.size : Vector2.zero;

            BuildSites();
            BuildSyntheticGrid();

            voronoi = new Voronoi(sites, gridSize, nodeGrid);
            paintedCellColors = new Color[gridSize.x, gridSize.y];
            hasPaintedCellColor = new bool[gridSize.x, gridSize.y];

            lastShowBoundaries = showBoundaries;
            lastShowSeeds = showSeeds;
        }

        void BuildSites()
        {
            Point[] sourcePoints = pointsOfInterest ?? new Point[0];
            sites = new PointOfInterest[sourcePoints.Length];

            sitePaintDataById.Clear();
            siteIdByGridPos.Clear();

            for (int i = 0; i < sourcePoints.Length; i++)
            {
                Vector2 normalizedPoint = sourcePoints[i] != null ? sourcePoints[i].p : Vector2.zero;
                int x = Mathf.Clamp(Mathf.RoundToInt(normalizedPoint.x * (gridSize.x - 1)), 0, gridSize.x - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(normalizedPoint.y * (gridSize.y - 1)), 0, gridSize.y - 1);

                PointOfInterest site = new PointOfInterest
                {
                    id = i,
                    gridPos = new Vector2Int(x, y),
                    t = null
                };

                sites[i] = site;

                SitePaintData sitePaintData = new SitePaintData
                {
                    id = site.id,
                    gridPos = site.gridPos,
                    color = GetSiteColor(i, sourcePoints.Length)
                };

                sitePaintDataById[site.id] = sitePaintData;
                siteIdByGridPos[site.gridPos] = site.id;
            }
        }

        void BuildSyntheticGrid()
        {
            nodeGrid = new PathNode[gridSize.x, gridSize.y];

            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                    nodeGrid[x, y] = new PathNode(true, new Vector3(x, 0f, y), new Vector2Int(x, y), 0);
            }
        }

        Color GetSiteColor(int index, int totalSiteCount)
        {
            if (colors != null && index < colors.Length)
                return colors[index];

            if (colors != null && colors.Length > 0)
                return colors[index % colors.Length];

            float hue = totalSiteCount > 0 ? (float)index / totalSiteCount : 0f;
            return Color.HSVToRGB(hue, 0.8f, 1f);
        }

        bool TryGetDebugGridPosition(out Vector2Int debugGridPos)
        {
            debugGridPos = default;

            if (debugPoint == null || debugWorldCenter == null)
                return false;

            if (debugWorldSize.x <= 0f || debugWorldSize.y <= 0f)
                return false;

            Vector3 center = debugWorldCenter.position;
            Vector3 worldPos = debugPoint.position;

            float percentX = (worldPos.x - center.x + debugWorldSize.x * 0.5f) / debugWorldSize.x;
            float percentY = (worldPos.z - center.z + debugWorldSize.y * 0.5f) / debugWorldSize.y;

            if (percentX < 0f || percentX > 1f || percentY < 0f || percentY > 1f)
                return false;

            int x = Mathf.RoundToInt((gridSize.x - 1) * percentX);
            int y = Mathf.RoundToInt((gridSize.y - 1) * percentY);
            debugGridPos = new Vector2Int(x, y);
            return true;
        }

        void RenderDiagram()
        {
            if (img == null)
                return;

            Vector2 currentImageSize = img.rectTransform.rect.size;
            imgSize = currentImageSize;

            int texW = Mathf.Max(1, Mathf.RoundToInt(currentImageSize.x));
            int texH = Mathf.Max(1, Mathf.RoundToInt(currentImageSize.y));
            if (texture == null || texture.width != texW || texture.height != texH)
            {
                if (texture != null)
                    Destroy(texture);

                texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point
                };
            }

            for (int x = 0; x < texW; x++)
            {
                for (int y = 0; y < texH; y++)
                    texture.SetPixel(x, y, unpaintedColor);
            }

            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    if (!hasPaintedCellColor[x, y])
                        continue;

                    PaintCell(texture, new Vector2Int(x, y), paintedCellColors[x, y], texW, texH);
                }
            }

            if (showBoundaries)
                DrawVoronoiBoundaries(texture, texW, texH);

            if (showSeeds)
                DrawSeeds(texture, texW, texH);

            texture.Apply();
            img.texture = texture;
        }

        void PaintCell(Texture2D tex, Vector2Int cell, Color color, int texW, int texH)
        {
            int texX0 = Mathf.RoundToInt((float)cell.x / gridSize.x * texW);
            int texX1 = Mathf.RoundToInt((float)(cell.x + 1) / gridSize.x * texW);
            int texY0 = Mathf.RoundToInt((float)cell.y / gridSize.y * texH);
            int texY1 = Mathf.RoundToInt((float)(cell.y + 1) / gridSize.y * texH);

            for (int px = texX0; px < texX1; px++)
            {
                for (int py = texY0; py < texY1; py++)
                    tex.SetPixel(px, py, color);
            }
        }

        void DrawVoronoiBoundaries(Texture2D tex, int texW, int texH)
        {
            foreach (Voronoi.VoronoiSite siteData in voronoi.SitesByPoi.Values)
            {
                List<Vector2> polygon = siteData.polygonVertices;
                if (polygon == null || polygon.Count < 2)
                    continue;

                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 from = polygon[i];
                    Vector2 to = polygon[(i + 1) % polygon.Count];
                    DrawLine(tex, GridToTex(from, texW, texH), GridToTex(to, texW, texH), Color.black);
                }
            }
        }

        void DrawSeeds(Texture2D tex, int texW, int texH)
        {
            foreach (SitePaintData sitePaintData in sitePaintDataById.Values)
            {
                Vector2Int seedPos = GridToTex(sitePaintData.gridPos, texW, texH);
                DrawDot(tex, seedPos.x, seedPos.y, seedDotRadius, Color.white);
                DrawDot(tex, seedPos.x, seedPos.y, Mathf.Max(1, seedDotRadius - 1), Color.black);
            }
        }

        Vector2Int GridToTex(Vector2 gridPos, int texW, int texH)
        {
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(gridPos.x / gridSize.x * texW), 0, texW - 1),
                Mathf.Clamp(Mathf.RoundToInt(gridPos.y / gridSize.y * texH), 0, texH - 1));
        }

        void DrawLine(Texture2D tex, Vector2Int p0, Vector2Int p1, Color color)
        {
            int x0 = p0.x;
            int y0 = p0.y;
            int x1 = p1.x;
            int y1 = p1.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < tex.width && y0 >= 0 && y0 < tex.height)
                    tex.SetPixel(x0, y0, color);

                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        void DrawDot(Texture2D tex, int centerX, int centerY, int radius, Color color)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius)
                        continue;

                    tex.SetPixel(
                        Mathf.Clamp(centerX + dx, 0, tex.width - 1),
                        Mathf.Clamp(centerY + dy, 0, tex.height - 1),
                        color);
                }
            }
        }

        public List<int> FindPathThroughRegions(int startRegionId, int goalRegionId)
        {
            throw new System.NotImplementedException(
                "A* Voronoi pathfinding will be implemented in the next phase.");
        }

        [ContextMenu("Draw All")]
        public void DrawAll()
        {
            for (int i = 0; i < gridSize.x; i++)
            {
                for (int j = 0; j < gridSize.y; j++)
                {
                    Vector2Int gridPos = new Vector2Int(i, j);
                    Vector2Int closestSiteGridPos = voronoi.GetClosestSite(gridPos);
                    if (siteIdByGridPos.TryGetValue(closestSiteGridPos, out int siteId) &&
                        sitePaintDataById.TryGetValue(siteId, out SitePaintData sitePaintData))
                    {
                        if (!hasPaintedCellColor[gridPos.x, gridPos.y] ||
                            paintedCellColors[gridPos.x, gridPos.y] != sitePaintData.color)
                        {
                            paintedCellColors[gridPos.x, gridPos.y] = sitePaintData.color;
                            hasPaintedCellColor[gridPos.x, gridPos.y] = true;
                        }
                    }
                }
            }
            
            
            RenderDiagram();
        }
    }
}
