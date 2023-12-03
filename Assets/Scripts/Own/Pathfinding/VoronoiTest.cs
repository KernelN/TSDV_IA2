using UnityEngine;
using UnityEngine.UI;

namespace IA.Pathfinding.Voronoi
{
    public class VoronoiTest : MonoBehaviour
    {
        [System.Serializable]
        public class Point
        {
            public Vector2 p;
        }
        
        [Header("Set Values")] 
        [SerializeField] Vector2Int points;
        [SerializeField] Point[] pointsOfInterest;
        [SerializeField] RawImage img;
        [SerializeField] Color[] colors;
        [Header("Runtime Values")]
        [SerializeField] Vector2 imgSize;
        [SerializeField] Vector2Int[,] pointsPos;
        [SerializeField] Color[,] pointsColor;
        [SerializeField] Vector2[] posOfInterest;
        [SerializeField] Color[] colorsOfInterest;

        //Unity Events
        void Start()
        {
            imgSize = img.rectTransform.sizeDelta;
            //pixelsPerCell = imgSize / gridSize;
            GeneratePoints();
            GenerateDiagram();
        }

        //Methods
        void GeneratePoints()
        {
            colorsOfInterest = new Color[pointsOfInterest.Length];
            posOfInterest = new Vector2[pointsOfInterest.Length];

            bool needsRandomColors = colorsOfInterest.Length > colors.Length;
            if (!needsRandomColors)
                colorsOfInterest = colors;
            
            for (int i = 0; i < pointsOfInterest.Length; i++)
            {
                if (needsRandomColors)
                {
                    if(i < colors.Length)
                        colorsOfInterest[i] = colors[i];
                    else
                        colorsOfInterest[i] = colors[Random.Range(0, colors.Length)];
                }
                posOfInterest[i].x = pointsOfInterest[i].p.x * points.x;
                posOfInterest[i].y = pointsOfInterest[i].p.y * points.y;
            }
            
            pointsPos = new Vector2Int[points.x, points.y];
            pointsColor = new Color[points.x, points.y];
            for (int i = 0; i < points.x; i++)
            {
                for (int j = 0; j < points.y; j++)
                {
                    float nearestDistance = float.MaxValue;
                    int nearestPoint = 0;

                    for (int k = 0; k < pointsOfInterest.Length; k++)
                    {
                        float distance = Vector2.Distance(new Vector2(i, j), posOfInterest[k]);
                        //distance /= pointsOfInterest[k].w;
                        
                        if(distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestPoint = k;
                        }
                    }

                    Vector2Int imgPos = new Vector2Int();
                    imgPos.x = (int)(((float)i / points.x) * imgSize.x);
                    imgPos.y = (int)(((float)j / points.y) * imgSize.y);
                    pointsPos[i, j] = imgPos;
                    pointsColor[i, j] = colorsOfInterest[nearestPoint];
                }
            }

            // pointsPos = new Vector2Int[gridSize, gridSize];
            // pointsColor = new Color[gridSize, gridSize];
            // for (int i = 0; i < gridSize; i++)
            // {
            //     for (int j = 0; j < gridSize; j++)
            //     {
            //         int x = i * gridSize + Random.Range(0, pixelsPerCell);
            //         int y = j * gridSize + Random.Range(0, pixelsPerCell);
            //         pointsPos[i, j] = new Vector2Int(x, y);
            //         pointsColor[i, j] = colors[Random.Range(0, colors.Length)];
            //     }
            // }
        }
        void GenerateDiagram()
        {
            Texture2D tex = new Texture2D((int)imgSize.x, (int)imgSize.y);

            //DrawPoints(tex);

            DrawDiagram(tex);

            tex.Apply();
            img.texture = tex;
        }
        void DrawPoints(Texture2D tex)
        {
            for (int i = 0; i < imgSize.x; i++)
            {
                for (int j = 0; j < imgSize.y; j++)
                {
                    tex.SetPixel(i, j, Color.white);
                }
            }
            
            for (int i = 0; i < points.x; i++)
            {
                for (int j = 0; j < points.y; j++)
                {
                    Vector2 pos = pointsPos[i, j];
                    tex.SetPixel((int)pos.x, (int)pos.y, pointsColor[i,j]);
                }
            }
        }
        void DrawDiagram(Texture2D tex)
        {
            for (int i = 0; i < points.x; i++)
            {
                for (int j = 0; j < points.y; j++)
                {
                    Vector2Int pos = pointsPos[i, j];
                    tex.SetPixel(pos.x, pos.y, pointsColor[i,j]);
                }
            }
        }
    }
}