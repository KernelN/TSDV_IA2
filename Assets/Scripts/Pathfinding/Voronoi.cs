using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    //https://demonstrations.wolfram.com/AlphaComplexAndUnionOfGrowingDisks/
    //Great for visualization
    public class Voronoi
    {
        const float PolygonEpsilon = 0.0001f;
        const float DistanceTieEpsilon = 0.01f;

        public class VoronoiVertex
        {
            public Vector2Int gridPos;
            public Vector2 pos;
            public List<VoronoiSite> linkedSites;
            public bool touchesMapBoundary;
            public Vector2 boundaryNormal;
            public Vector2 boundaryAxis;

            public VoronoiVertex(Vector2Int gridPos, Vector2 pos, List<VoronoiSite> linkedSites,
                bool touchesMapBoundary, Vector2 boundaryNormal, Vector2 boundaryAxis)
            {
                this.gridPos = gridPos;
                this.pos = pos;
                this.linkedSites = linkedSites ?? new List<VoronoiSite>();
                this.touchesMapBoundary = touchesMapBoundary;
                this.boundaryNormal = boundaryNormal;
                this.boundaryAxis = boundaryAxis;
            }
        }

        public class VoronoiSite
        {
            public PointOfInterest site;
            public Vector2Int gridPos => site.gridPos;
            public List<Vector2> polygonVertices;
            public List<VoronoiVertex> polygonVertexObjects;

            public VoronoiSite(PointOfInterest site)
            {
                this.site = site;
                polygonVertices = new List<Vector2>();
                polygonVertexObjects = new List<VoronoiVertex>();
            }
        }

        PointOfInterest[] sites;
        Vector2Int mapSize;
        PathNode[,] nodeGrid;

        List<VoronoiSite> validSites = new List<VoronoiSite>();
        Dictionary<PointOfInterest, VoronoiSite> sitesByPoi = new Dictionary<PointOfInterest, VoronoiSite>();
        List<VoronoiVertex> vertices = new List<VoronoiVertex>();

        int voronoiVersion;

        public int VoronoiVersion => voronoiVersion;
        public IReadOnlyDictionary<PointOfInterest, VoronoiSite> SitesByPoi => sitesByPoi;

        public Voronoi(PointOfInterest[] sites, Vector2Int mapSize, PathNode[,] nodeGrid)
        {
            this.sites = sites ?? new PointOfInterest[0];
            this.mapSize = new Vector2Int(Mathf.Max(1, mapSize.x), Mathf.Max(1, mapSize.y));
            this.nodeGrid = nodeGrid;

            voronoiVersion = 0;
            
            CalculateFullVoronoi();
        }

        public void CalculateFullVoronoi()
        {
            // Every full rebuild creates a new version. Nodes keep the version number of
            // their cached closest site, so old answers become stale automatically.
            voronoiVersion++;
            RebuildAllVoronoiData();
        }

        public Vector2Int GetClosestSite(Vector2Int nodeGridPosition)
        {
            // If the requested node is outside the known grid, there is no safe site to return.
            if (!IsGridPositionValid(nodeGridPosition) || validSites.Count == 0)
                return new Vector2Int(-1, -1);

            PathNode node = nodeGrid[nodeGridPosition.x, nodeGridPosition.y];
            if (node == null)
                return new Vector2Int(-1, -1);

            // A node can reuse its previous answer as long as the Voronoi version still matches.
            if (node.hasClosestVoronoiSite && node.closestVoronoiVersion == voronoiVersion)
                return node.closestVoronoiSiteGridPos;

            List<VoronoiSite> sortedSites = new List<VoronoiSite>(validSites);
            sortedSites.Sort((firstSite, secondSite) =>
            {
                float firstDistance = GetSquaredEuclideanDistance(nodeGridPosition, firstSite.gridPos);
                float secondDistance = GetSquaredEuclideanDistance(nodeGridPosition, secondSite.gridPos);
                if (!Mathf.Approximately(firstDistance, secondDistance))
                    return firstDistance.CompareTo(secondDistance);

                int xComparison = firstSite.gridPos.x.CompareTo(secondSite.gridPos.x);
                if (xComparison != 0)
                    return xComparison;

                int yComparison = firstSite.gridPos.y.CompareTo(secondSite.gridPos.y);
                if (yComparison != 0)
                    return yComparison;

                return firstSite.site.id.CompareTo(secondSite.site.id);
            });

            Vector2 pointInGridSpace = new Vector2(nodeGridPosition.x, nodeGridPosition.y);
            for (int i = 0; i < sortedSites.Count; i++)
            {
                VoronoiSite candidateSite = sortedSites[i];

                // The polygon check is a 2D ray-crossing test in grid space.
                // It is called a raycast here because a horizontal ray is counted against polygon edges.
                if (!IsPointInsidePolygon(pointInGridSpace, candidateSite.polygonVertices))
                    continue;

                CacheClosestSiteOnNode(node, candidateSite.gridPos);
                return candidateSite.gridPos;
            }

            // If no polygon catches the node, the nearest sorted site keeps the query useful and deterministic.
            // This can happen while a site is invalid or duplicated and therefore has no polygon.
            VoronoiSite fallbackSite = sortedSites[0];
            CacheClosestSiteOnNode(node, fallbackSite.gridPos);
            return fallbackSite.gridPos;
        }

        public void CalculatePartialVoronoi(PointOfInterest removedSite)
        {
            // A partial rebuild also creates a new version so cached node answers expire.
            voronoiVersion++;

            // Delaunay neighbours can change outside the removed site's old local links.
            // A full rebuild is easier to trust for this review step, and a smaller partial Delaunay update can be added later.
            RebuildAllVoronoiData();
        }

        void RebuildAllVoronoiData()
        {
            // The site dictionary is rebuilt first so every later step can use clean lookups.
            validSites.Clear();
            sitesByPoi.Clear();
            vertices.Clear();

            HashSet<Vector2Int> usedGridPositions = new HashSet<Vector2Int>();
            for (int i = 0; i < sites.Length; i++)
            {
                PointOfInterest poi = sites[i];
                if (poi == null) continue;

                VoronoiSite site = new VoronoiSite(poi);
                sitesByPoi.TryAdd(poi, site);

                //Make sure to skip duplicate site positions, to avoid delanuay exploding 
                if (!IsGridPositionValid(poi.gridPos) || !usedGridPositions.Add(poi.gridPos))
                    continue;

                validSites.Add(site);
            }

            // Calculate Delanuay / inverted voronoi
            // The Delaunay triangles are now the source of the Voronoi vertices.
            // This follows the Delaunay/Voronoi dual rule: each triangle circumcenter becomes a Voronoi vertex.
            List<DelaunayTriangle> delaunayTriangles = BuildDelaunayTriangles();

            // The polygon rebuild collects circumcenters and boundary support vertices,
            // then each site sorts its own collected points into a closed loop.
            RebuildSitePolygons(delaunayTriangles);
        }

        void RebuildSitePolygons(List<DelaunayTriangle> delaunayTriangles)
        {
            // Start from empty polygon data so every rebuild is fully deterministic.
            // The vertex list is global, while each site keeps its own ordered view of those vertices.
            vertices.Clear();
            ClearSitePolygons();

            if (validSites.Count == 0)
                return;

            // A single site owns the entire map because no bisector can split the rectangle.
            if (validSites.Count == 1)
            {
                StorePolygonVerticesForSite(validSites[0], CreateMapRectanglePolygon());
                return;
            }

            // Two sites do not create any Delaunay triangle, so their shared border is the
            // perpendicular bisector bounded only by the map rectangle endpoints.
            if (validSites.Count == 2)
            {
                RebuildTwoSitePolygons(validSites[0], validSites[1]);
                return;
            }

            if (delaunayTriangles == null || delaunayTriangles.Count == 0)
                return;

            // Interior Voronoi vertices are the circumcenters of the real Delaunay triangles.
            // They are linked to the three sites that created each triangle.
            AddInteriorCircumcenterVertices(delaunayTriangles);

            // Hull edges are used by only one triangle. Their Voronoi edges are open rays,
            // so mirrored sites across the rectangle create the bounded support vertex.
            AddBoundaryVerticesForOpenDelaunayEdges(delaunayTriangles);

            // Corners are not circumcenters, but they are needed when a cell owns a rectangle corner.
            // Adding them after the edge vertices keeps every site polygon closed along the map border.
            AddMapCornersToClosestSites();
            FinalizeSitePolygons();
        }

        List<DelaunayTriangle> BuildDelaunayTriangles()
        {
            List<DelaunayTriangle> triangulation = new List<DelaunayTriangle>();
            triangulation.Add(CreateSuperTriangle()); //Add a triangle that holds the whole map first

            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite insertedSite = validSites[i];
                Vector2 sitePos = GetSiteGridPosition(insertedSite);

                // Run through all triangles,
                // if any of their circumcircle contain the new site,
                // the triangle is not valid and will be removed
                List<DelaunayTriangle> badTriangles = new List<DelaunayTriangle>();
                for (int triangleIndex = 0; triangleIndex < triangulation.Count; triangleIndex++)
                {
                    if (triangulation[triangleIndex].CircumcircleContains(sitePos))
                        badTriangles.Add(triangulation[triangleIndex]);
                }

                //Store the unique lines of the bad tris so they can be used later
                //if a line is unique, it means it is on
                //the edge of the polygon hole left by the bad triangles
                List<DelaunayEdge> polygonHoleEdges = new List<DelaunayEdge>();
                for (int badIndex = 0; badIndex < badTriangles.Count; badIndex++)
                {
                    List<DelaunayEdge> badTriangleEdges = badTriangles[badIndex].GetEdges();
                    for (int edgeIndex = 0; edgeIndex < badTriangleEdges.Count; edgeIndex++)
                    {
                        DelaunayEdge candidateEdge = badTriangleEdges[edgeIndex];
                        bool isSharedByAnotherBadTriangle = false;

                        for (int otherBadIndex = 0; otherBadIndex < badTriangles.Count; otherBadIndex++)
                        {
                            if (otherBadIndex == badIndex) continue;

                            if (badTriangles[otherBadIndex].HasEdge(candidateEdge))
                            {
                                isSharedByAnotherBadTriangle = true;
                                break;
                            }
                        }

                        //If the line/edge is shared by another bad tri,
                        //it means it is inside the polygon hole
                        if (!isSharedByAnotherBadTriangle)
                            polygonHoleEdges.Add(candidateEdge);
                    }
                }

                //Remove all bad triangles
                for (int badIndex = 0; badIndex < badTriangles.Count; badIndex++)
                    triangulation.Remove(badTriangles[badIndex]);

                // Make new triangles using the borders of the hole and the new site
                for (int edgeIndex = 0; edgeIndex < polygonHoleEdges.Count; edgeIndex++)
                {
                    DelaunayEdge edge = polygonHoleEdges[edgeIndex];
                    DelaunayTriangle newTriangle = new DelaunayTriangle(
                        edge.posA, edge.posB, sitePos,
                        edge.siteA, edge.siteB, insertedSite);

                    //If the new triangle is valid, add it to the list
                    if (!newTriangle.IsTooSmall())
                        triangulation.Add(newTriangle);
                }
            }

            //Remove the original super triangle
            //(as it was only needed to calculate the inner tris),
            //and any invalid triangle
            triangulation.RemoveAll(triangle => triangle.HasSuperVertex() || triangle.IsTooSmall());
            return triangulation;
        }

        DelaunayTriangle CreateSuperTriangle()
        {
            // The super-triangle only needs to fully contain the map and every site.
            // A large margin makes the first Bowyer-Watson insertion easy and stable.
            float width = mapSize.x;
            float height = mapSize.y;
            float margin = Mathf.Max(width, height);

            Vector2 top = new Vector2(width * 0.5f, height + margin);
            Vector2 bottomLeft = new Vector2(-margin, -margin);
            Vector2 bottomRight = new Vector2(width + margin, -margin);

            return new DelaunayTriangle(top, bottomLeft, bottomRight, null, null, null);
        }

        void ClearSitePolygons()
        {
            // Sites keep their own polygon lists, so clear each one before adding shared vertices again.
            for (int i = 0; i < validSites.Count; i++)
            {
                validSites[i].polygonVertexObjects.Clear();
                validSites[i].polygonVertices.Clear();
            }
        }

        void AddInteriorCircumcenterVertices(List<DelaunayTriangle> delaunayTriangles)
        {
            for (int i = 0; i < delaunayTriangles.Count; i++)
            {
                DelaunayTriangle triangle = delaunayTriangles[i];
                List<VoronoiSite> linkedSites = triangle.GetSites();
                if (linkedSites.Count != 3)
                    continue;

                // The Delaunay/Voronoi dual says this circumcenter is shared by the three triangle sites.
                // It replaces the old midpoint vertices with the real Voronoi vertex.
                GetOrCreateSharedVertex(
                    triangle.GetCircumcenter(),
                    linkedSites,
                    false,
                    Vector2.zero,
                    Vector2.zero,
                    false);
            }
        }

        void AddBoundaryVerticesForOpenDelaunayEdges(List<DelaunayTriangle> delaunayTriangles)
        {
            // Counting Delaunay edges reveals the convex hull.
            // Interior edges are used by two triangles, while hull edges are used by only one.
            List<DelaunayEdgeUse> edgeUses = BuildDelaunayEdgeUses(delaunayTriangles);
            for (int i = 0; i < edgeUses.Count; i++)
            {
                DelaunayEdgeUse edgeUse = edgeUses[i];
                if (edgeUse.useCount != 1)
                    continue;

                AddBoundaryVertexForOpenDelaunayEdge(edgeUse.edge, edgeUse.triangle);
            }
        }

        List<DelaunayEdgeUse> BuildDelaunayEdgeUses(List<DelaunayTriangle> delaunayTriangles)
        {
            List<DelaunayEdgeUse> edgeUses = new List<DelaunayEdgeUse>();
            for (int i = 0; i < delaunayTriangles.Count; i++)
            {
                DelaunayTriangle triangle = delaunayTriangles[i];
                List<DelaunayEdge> edges = triangle.GetEdges();
                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    DelaunayEdge edge = edges[edgeIndex];
                    if (edge.siteA == null || edge.siteB == null)
                        continue;

                    // The same Delaunay edge can be listed in either direction,
                    // so use the edge comparison helper instead of relying on order.
                    DelaunayEdgeUse existingEdgeUse = FindDelaunayEdgeUse(edgeUses, edge);
                    if (existingEdgeUse != null)
                    {
                        existingEdgeUse.useCount++;
                        continue;
                    }

                    edgeUses.Add(new DelaunayEdgeUse(edge, triangle));
                }
            }

            return edgeUses;
        }

        DelaunayEdgeUse FindDelaunayEdgeUse(List<DelaunayEdgeUse> edgeUses, DelaunayEdge edge)
        {
            for (int i = 0; i < edgeUses.Count; i++)
            {
                if (edgeUses[i].edge.IsSameEdge(edge))
                    return edgeUses[i];
            }

            return null;
        }

        void AddBoundaryVertexForOpenDelaunayEdge(DelaunayEdge edge, DelaunayTriangle triangle)
        {
            if (edge == null || triangle == null || edge.siteA == null || edge.siteB == null)
                return;

            if (!triangle.TryGetSiteOutsideEdge(edge, out VoronoiSite oppositeSite))
                return;

            //Inspired by: https://stackoverflow.com/questions/59869376/calculating-the-infinity-points-edges-of-the-delaunay-triangulation
            // Mirroring one parent site across a rectangle side creates a triangle whose circumcenter lands on that side.
            List<VoronoiSite> linkedSites = new List<VoronoiSite> { edge.siteA, edge.siteB };
            if (!TryGetMirroredBoundaryVertex(edge, triangle, oppositeSite,
                    out Vector2 boundaryPosition, out Vector2 boundaryNormal, out Vector2 boundaryAxis))
            {
                // If the mirror triangle is flat or misses the rectangle side, use the same answer's fallback idea:
                // send the open Voronoi ray from the triangle circumcenter through the hull edge and intersect the box.
                if (!TryProjectOpenEdgeToMapBoundary(edge, triangle, oppositeSite,
                        out boundaryPosition, out boundaryNormal, out boundaryAxis))
                    return;
            }

            VoronoiVertex boundaryVertex = GetOrCreateSharedVertex(
                boundaryPosition,
                linkedSites,
                true,
                boundaryNormal,
                boundaryAxis,
                true);

            AddVertexToSite(edge.siteA, boundaryVertex);
            AddVertexToSite(edge.siteB, boundaryVertex);
        }

        bool TryGetMirroredBoundaryVertex(DelaunayEdge edge, DelaunayTriangle triangle, VoronoiSite oppositeSite,
            out Vector2 boundaryPosition, out Vector2 boundaryNormal, out Vector2 boundaryAxis)
        {
            boundaryPosition = Vector2.zero;
            boundaryNormal = Vector2.zero;
            boundaryAxis = Vector2.zero;

            List<BoundarySide> boundarySides = CreateBoundarySides();
            Vector2 triangleCenter = triangle.GetCircumcenter();
            float bestDistanceSqr = float.MaxValue;
            bool foundCandidate = false;

            for (int sideIndex = 0; sideIndex < boundarySides.Count; sideIndex++)
            {
                BoundarySide side = boundarySides[sideIndex];

                // Try mirroring both parent sites. Usually one mirror is enough,
                // but trying both avoids losing the point when the first mirrored triangle is nearly flat.
                TryUseMirroredSiteCandidate(edge, oppositeSite, side, triangleCenter, edge.siteA,
                    ref foundCandidate, ref bestDistanceSqr, ref boundaryPosition, ref boundaryNormal, ref boundaryAxis);
                TryUseMirroredSiteCandidate(edge, oppositeSite, side, triangleCenter, edge.siteB,
                    ref foundCandidate, ref bestDistanceSqr, ref boundaryPosition, ref boundaryNormal, ref boundaryAxis);
            }

            return foundCandidate;
        }

        void TryUseMirroredSiteCandidate(DelaunayEdge edge, VoronoiSite oppositeSite, BoundarySide side,
            Vector2 triangleCenter, VoronoiSite mirroredSite, ref bool foundCandidate, ref float bestDistanceSqr,
            ref Vector2 boundaryPosition, ref Vector2 boundaryNormal, ref Vector2 boundaryAxis)
        {
            Vector2 siteAPosition = GetSiteGridPosition(edge.siteA);
            Vector2 siteBPosition = GetSiteGridPosition(edge.siteB);
            Vector2 mirroredPosition = MirrorPointAcrossBoundarySide(GetSiteGridPosition(mirroredSite), side);

            DelaunayTriangle mirroredTriangle = new DelaunayTriangle(
                siteAPosition,
                siteBPosition,
                mirroredPosition,
                edge.siteA,
                edge.siteB,
                null);

            if (mirroredTriangle.IsTooSmall())
                return;

            Vector2 candidatePosition = mirroredTriangle.GetCircumcenter();
            if (!IsPointOnBoundarySideSegment(candidatePosition, side))
                return;

            if (!IsBoundaryCandidateOutsideHullEdge(edge, oppositeSite, candidatePosition))
                return;

            float candidateDistanceSqr = (candidatePosition - triangleCenter).sqrMagnitude;
            if (foundCandidate && candidateDistanceSqr >= bestDistanceSqr)
                return;

            foundCandidate = true;
            bestDistanceSqr = candidateDistanceSqr;
            boundaryPosition = ClampPointToBoundarySide(candidatePosition, side);
            boundaryNormal = side.normal;
            boundaryAxis = side.axis;
        }

        bool TryProjectOpenEdgeToMapBoundary(DelaunayEdge edge, DelaunayTriangle triangle, VoronoiSite oppositeSite,
            out Vector2 boundaryPosition, out Vector2 boundaryNormal, out Vector2 boundaryAxis)
        {
            boundaryPosition = Vector2.zero;
            boundaryNormal = Vector2.zero;
            boundaryAxis = Vector2.zero;

            // The open ray points away from the third triangle site and through the hull edge midpoint.
            // That gives the same direction described for the infinite Voronoi edge fallback.
            Vector2 rayStart = triangle.GetCircumcenter();
            Vector2 edgeMidpoint = (edge.posA + edge.posB) * 0.5f;
            Vector2 oppositePosition = GetSiteGridPosition(oppositeSite);
            Vector2 rayDirection = edgeMidpoint - oppositePosition;
            if (rayDirection.sqrMagnitude <= PolygonEpsilon)
                rayDirection = edgeMidpoint - rayStart;

            if (rayDirection.sqrMagnitude <= PolygonEpsilon)
                return false;

            return TryIntersectRayWithMapBoundary(rayStart, rayDirection.normalized,
                out boundaryPosition, out boundaryNormal, out boundaryAxis);
        }

        bool TryIntersectRayWithMapBoundary(Vector2 rayStart, Vector2 rayDirection,
            out Vector2 boundaryPosition, out Vector2 boundaryNormal, out Vector2 boundaryAxis)
        {
            boundaryPosition = Vector2.zero;
            boundaryNormal = Vector2.zero;
            boundaryAxis = Vector2.zero;

            List<BoundarySide> boundarySides = CreateBoundarySides();
            float bestRayPercent = float.MaxValue;
            bool foundIntersection = false;

            for (int i = 0; i < boundarySides.Count; i++)
            {
                BoundarySide side = boundarySides[i];
                if (!TryGetRayBoundarySideIntersection(rayStart, rayDirection, side,
                        out Vector2 candidatePosition, out float rayPercent))
                    continue;

                if (rayPercent <= PolygonEpsilon || rayPercent >= bestRayPercent)
                    continue;

                foundIntersection = true;
                bestRayPercent = rayPercent;
                boundaryPosition = candidatePosition;
                boundaryNormal = side.normal;
                boundaryAxis = side.axis;
            }

            return foundIntersection;
        }

        bool TryGetRayBoundarySideIntersection(Vector2 rayStart, Vector2 rayDirection, BoundarySide side,
            out Vector2 candidatePosition, out float rayPercent)
        {
            candidatePosition = Vector2.zero;
            rayPercent = 0f;

            if (side.isVertical)
            {
                if (Mathf.Abs(rayDirection.x) <= PolygonEpsilon)
                    return false;

                rayPercent = (side.coordinate - rayStart.x) / rayDirection.x;
                candidatePosition = rayStart + rayDirection * rayPercent;
            }
            else
            {
                if (Mathf.Abs(rayDirection.y) <= PolygonEpsilon)
                    return false;

                rayPercent = (side.coordinate - rayStart.y) / rayDirection.y;
                candidatePosition = rayStart + rayDirection * rayPercent;
            }

            if (rayPercent <= PolygonEpsilon)
                return false;

            if (!IsPointOnBoundarySideSegment(candidatePosition, side))
                return false;

            candidatePosition = ClampPointToBoundarySide(candidatePosition, side);
            return true;
        }

        bool IsBoundaryCandidateOutsideHullEdge(DelaunayEdge edge, VoronoiSite oppositeSite, Vector2 candidatePosition)
        {
            // The candidate must be on the opposite side of the hull edge from the third triangle site.
            // This keeps the finite boundary point on the same side as the open Voronoi ray.
            Vector2 edgeDirection = edge.posB - edge.posA;
            Vector2 oppositeDirection = GetSiteGridPosition(oppositeSite) - edge.posA;
            Vector2 candidateDirection = candidatePosition - edge.posA;

            float oppositeSide = Cross(edgeDirection, oppositeDirection);
            float candidateSide = Cross(edgeDirection, candidateDirection);
            return oppositeSide * candidateSide <= PolygonEpsilon;
        }

        Vector2 MirrorPointAcrossBoundarySide(Vector2 point, BoundarySide side)
        {
            // Mirroring across a vertical side changes only X; mirroring across a horizontal side changes only Y.
            if (side.isVertical)
                return new Vector2(2f * side.coordinate - point.x, point.y);

            return new Vector2(point.x, 2f * side.coordinate - point.y);
        }

        bool IsPointOnBoundarySideSegment(Vector2 point, BoundarySide side)
        {
            if (side.isVertical)
            {
                bool isOnSideLine = Mathf.Abs(point.x - side.coordinate) <= DistanceTieEpsilon;
                bool isInsideSideLimits = point.y >= side.minValue - DistanceTieEpsilon &&
                                          point.y <= side.maxValue + DistanceTieEpsilon;
                return isOnSideLine && isInsideSideLimits;
            }

            bool isOnHorizontalLine = Mathf.Abs(point.y - side.coordinate) <= DistanceTieEpsilon;
            bool isInsideHorizontalLimits = point.x >= side.minValue - DistanceTieEpsilon &&
                                            point.x <= side.maxValue + DistanceTieEpsilon;
            return isOnHorizontalLine && isInsideHorizontalLimits;
        }

        Vector2 ClampPointToBoundarySide(Vector2 point, BoundarySide side)
        {
            if (side.isVertical)
                return new Vector2(side.coordinate, Mathf.Clamp(point.y, side.minValue, side.maxValue));

            return new Vector2(Mathf.Clamp(point.x, side.minValue, side.maxValue), side.coordinate);
        }

        List<BoundarySide> CreateBoundarySides()
        {
            // Boundary normals point out of the rectangle.
            // The axis is perpendicular to the normal and tells later code how the vertex can slide along the edge.
            float maxX = mapSize.x - 1f;
            float maxY = mapSize.y - 1f;
            return new List<BoundarySide>
            {
                new BoundarySide(true, 0f, 0f, maxY, new Vector2(-1f, 0f), new Vector2(0f, 1f)),
                new BoundarySide(true, maxX, 0f, maxY, new Vector2(1f, 0f), new Vector2(0f, 1f)),
                new BoundarySide(false, 0f, 0f, maxX, new Vector2(0f, -1f), new Vector2(1f, 0f)),
                new BoundarySide(false, maxY, 0f, maxX, new Vector2(0f, 1f), new Vector2(1f, 0f))
            };
        }

        void RebuildTwoSitePolygons(VoronoiSite firstSite, VoronoiSite secondSite)
        {
            // With only two sites, the whole diagram is split by one perpendicular bisector.
            // The two points where that bisector touches the rectangle are the shared boundary vertices.
            List<VoronoiSite> linkedSites = new List<VoronoiSite> { firstSite, secondSite };
            List<VoronoiVertex> splitVertices = CreateTwoSiteSplitVertices(firstSite, secondSite, linkedSites);

            for (int i = 0; i < splitVertices.Count; i++)
            {
                AddVertexToSite(firstSite, splitVertices[i]);
                AddVertexToSite(secondSite, splitVertices[i]);
            }

            AddMapCornersToClosestSites();
            FinalizeSitePolygons();
        }

        List<VoronoiVertex> CreateTwoSiteSplitVertices(VoronoiSite firstSite, VoronoiSite secondSite,
            List<VoronoiSite> linkedSites)
        {
            List<VoronoiVertex> splitVertices = new List<VoronoiVertex>();
            Vector2 firstPosition = GetSiteGridPosition(firstSite);
            Vector2 secondPosition = GetSiteGridPosition(secondSite);
            Vector2 midpoint = (firstPosition + secondPosition) * 0.5f;
            Vector2 siteDirection = secondPosition - firstPosition;
            Vector2 bisectorDirection = new Vector2(-siteDirection.y, siteDirection.x);

            List<BoundarySide> boundarySides = CreateBoundarySides();
            for (int i = 0; i < boundarySides.Count; i++)
            {
                BoundarySide side = boundarySides[i];
                Vector2 sideStart = GetBoundarySideStart(side);
                Vector2 sideEnd = GetBoundarySideEnd(side);
                if (!TryGetLineIntersection(midpoint, midpoint + bisectorDirection,
                        sideStart, sideEnd, out Vector2 intersection))
                    continue;

                if (!IsPointOnBoundarySideSegment(intersection, side))
                    continue;

                VoronoiVertex splitVertex = GetOrCreateSharedVertex(
                    ClampPointToBoundarySide(intersection, side),
                    linkedSites,
                    true,
                    side.normal,
                    side.axis,
                    true);

                AddVertexIfMissing(splitVertices, splitVertex);
            }

            return splitVertices;
        }

        Vector2 GetBoundarySideStart(BoundarySide side)
        {
            if (side.isVertical)
                return new Vector2(side.coordinate, side.minValue);

            return new Vector2(side.minValue, side.coordinate);
        }

        Vector2 GetBoundarySideEnd(BoundarySide side)
        {
            if (side.isVertical)
                return new Vector2(side.coordinate, side.maxValue);

            return new Vector2(side.maxValue, side.coordinate);
        }

        void AddMapCornersToClosestSites()
        {
            // Rectangle corners are assigned to the nearest site so cells that touch corners draw along the box.
            // They are not marked as movable edge vertices because a corner belongs to two axes, not one.
            List<Vector2> mapCorners = CreateMapRectanglePolygon();
            for (int i = 0; i < mapCorners.Count; i++)
            {
                Vector2 corner = mapCorners[i];
                List<VoronoiSite> linkedSites = CalculateLinkedSitesForVertex(corner);
                VoronoiVertex cornerVertex = GetOrCreateSharedVertex(
                    corner,
                    linkedSites,
                    false,
                    Vector2.zero,
                    Vector2.zero,
                    true);

                for (int siteIndex = 0; siteIndex < linkedSites.Count; siteIndex++)
                    AddVertexToSite(linkedSites[siteIndex], cornerVertex);
            }
        }

        void FinalizeSitePolygons()
        {
            // Every site now has an unordered bag of shared vertices.
            // Sorting around the site turns that bag into the polygon loop used by drawing and point checks.
            for (int i = 0; i < validSites.Count; i++)
            {
                SortPolygonVertexObjectsAroundSite(validSites[i]);
                RemoveRepeatedPolygonVertexObjects(validSites[i]);
                SyncPolygonPositionsFromObjects(validSites[i]);
            }
        }

        void SortPolygonVertexObjectsAroundSite(VoronoiSite site)
        {
            if (site == null || site.polygonVertexObjects.Count < 2)
                return;

            Vector2 sitePosition = GetSiteGridPosition(site);
            site.polygonVertexObjects.Sort((firstVertex, secondVertex) =>
            {
                float firstAngle = Mathf.Atan2(firstVertex.pos.y - sitePosition.y, firstVertex.pos.x - sitePosition.x);
                float secondAngle = Mathf.Atan2(secondVertex.pos.y - sitePosition.y, secondVertex.pos.x - sitePosition.x);
                if (!Mathf.Approximately(firstAngle, secondAngle))
                    return firstAngle.CompareTo(secondAngle);

                float firstDistance = (firstVertex.pos - sitePosition).sqrMagnitude;
                float secondDistance = (secondVertex.pos - sitePosition).sqrMagnitude;
                return firstDistance.CompareTo(secondDistance);
            });
        }

        void RemoveRepeatedPolygonVertexObjects(VoronoiSite site)
        {
            if (site == null || site.polygonVertexObjects.Count < 2)
                return;

            for (int i = site.polygonVertexObjects.Count - 1; i >= 0; i--)
            {
                if (site.polygonVertexObjects.Count < 2)
                    break;

                int previousIndex = i == 0 ? site.polygonVertexObjects.Count - 1 : i - 1;
                if ((site.polygonVertexObjects[i].pos - site.polygonVertexObjects[previousIndex].pos).sqrMagnitude <= PolygonEpsilon)
                    site.polygonVertexObjects.RemoveAt(i);
            }
        }

        void SyncPolygonPositionsFromObjects(VoronoiSite site)
        {
            site.polygonVertices.Clear();
            for (int i = 0; i < site.polygonVertexObjects.Count; i++)
                site.polygonVertices.Add(site.polygonVertexObjects[i].pos);
        }

        bool TryGetLineIntersection(Vector2 firstLineStart, Vector2 firstLineEnd,
            Vector2 secondLineStart, Vector2 secondLineEnd, out Vector2 intersection)
        {
            // The 2D cross-product form solves where two infinite lines meet.
            // The bounded cases use it to find where a bisector reaches a map edge.
            intersection = Vector2.zero;

            Vector2 firstDirection = firstLineEnd - firstLineStart;
            Vector2 secondDirection = secondLineEnd - secondLineStart;
            float denominator = Cross(firstDirection, secondDirection);
            if (Mathf.Abs(denominator) <= PolygonEpsilon)
                return false;

            Vector2 startDifference = secondLineStart - firstLineStart;
            float firstLinePercent = Cross(startDifference, secondDirection) / denominator;
            intersection = firstLineStart + firstDirection * firstLinePercent;
            return true;
        }

        float Cross(Vector2 firstVector, Vector2 secondVector)
        {
            return firstVector.x * secondVector.y - firstVector.y * secondVector.x;
        }

        void StorePolygonVerticesForSite(VoronoiSite site, List<Vector2> polygon)
        {
            site.polygonVertexObjects.Clear();
            site.polygonVertices.Clear();

            if (polygon == null)
                return;

            for (int i = 0; i < polygon.Count; i++)
            {
                // The shared object is reused by every site that reaches the same point.
                // The Vector2 mirror is kept so the existing drawing and point-in-polygon code still works.
                VoronoiVertex vertex = GetOrCreateSharedVertex(
                    polygon[i],
                    CalculateLinkedSitesForVertex(polygon[i]),
                    false,
                    Vector2.zero,
                    Vector2.zero,
                    true);
                site.polygonVertexObjects.Add(vertex);
                site.polygonVertices.Add(vertex.pos);
            }
        }

        VoronoiVertex GetOrCreateSharedVertex(Vector2 vertexPosition, List<VoronoiSite> linkedSites,
            bool isBoundingVertex, Vector2 boundaryNormal, Vector2 boundaryAxis, bool clampToMapBounds)
        {
            // Boundary and corner vertices are clamped to the rectangle because they must sit on the map edge.
            // Interior circumcenters keep their true position, except for tiny floating-point drift near a boundary.
            Vector2 storedPosition = clampToMapBounds
                ? ClampPointToMapBounds(vertexPosition)
                : ClampPointToMapBoundsIfNearlyOnBoundary(vertexPosition);

            //Try to find vertex in existing list,
            //if it exists, add new sites to it and return it
            for (int i = 0; i < vertices.Count; i++)
            {
                if ((vertices[i].pos - storedPosition).sqrMagnitude > PolygonEpsilon)
                    continue;

                MergeLinkedSites(vertices[i], linkedSites);
                if (isBoundingVertex)
                {
                    vertices[i].touchesMapBoundary = true;
                    vertices[i].boundaryNormal = boundaryNormal;
                    vertices[i].boundaryAxis = boundaryAxis;
                }

                return vertices[i];
            }

            // Interior Voronoi vertices usually link three sites.
            // Mirrored boundary vertices link only the two real sites that own the open Delaunay edge.
            VoronoiVertex newVertex = new VoronoiVertex(
                RoundToGridPosition(storedPosition),
                storedPosition,
                linkedSites,
                IsPointOnMapBoundary(storedPosition) || isBoundingVertex,
                boundaryNormal,
                boundaryAxis);

            vertices.Add(newVertex);

            for (int i = 0; i < linkedSites.Count; i++)
                AddVertexToSite(linkedSites[i], newVertex);
            return newVertex;
        }

        void AddVertexToSite(VoronoiSite site, VoronoiVertex vertex)
        {
            if (site == null || vertex == null)
                return;

            // The same shared object can be reached through multiple triangles in co-circular cases.
            // Only add it once so sorting does not create zero-length polygon edges.
            AddVertexIfMissing(site.polygonVertexObjects, vertex);
        }

        void AddVertexIfMissing(List<VoronoiVertex> vertexList, VoronoiVertex vertex)
        {
            if (vertexList == null || vertex == null)
                return;

            for (int i = 0; i < vertexList.Count; i++)
            {
                if ((vertexList[i].pos - vertex.pos).sqrMagnitude <= PolygonEpsilon)
                    return;
            }

            vertexList.Add(vertex);
        }

        void MergeLinkedSites(VoronoiVertex vertex, List<VoronoiSite> linkedSites)
        {
            if (vertex == null || linkedSites == null)
                return;

            for (int i = 0; i < linkedSites.Count; i++)
            {
                VoronoiSite linkedSite = linkedSites[i];
                if (linkedSite == null || vertex.linkedSites.Contains(linkedSite))
                    continue;

                vertex.linkedSites.Add(linkedSite);
                
                if(!linkedSite.polygonVertexObjects.Contains(vertex))
                    linkedSite.polygonVertexObjects.Add(vertex);
            }
        }

        List<VoronoiSite> CalculateLinkedSitesForVertex(Vector2 vertexPosition)
        {
            List<VoronoiSite> linkedSites = new List<VoronoiSite>();
            float nearestDistanceSqr = float.MaxValue;

            for (int i = 0; i < validSites.Count; i++)
            {
                float distanceSqr = (GetSiteGridPosition(validSites[i]) - vertexPosition).sqrMagnitude;
                nearestDistanceSqr = Mathf.Min(nearestDistanceSqr, distanceSqr);
            }

            for (int i = 0; i < validSites.Count; i++)
            {
                float distanceSqr = (GetSiteGridPosition(validSites[i]) - vertexPosition).sqrMagnitude;
                if (Mathf.Abs(distanceSqr - nearestDistanceSqr) <= DistanceTieEpsilon)
                    linkedSites.Add(validSites[i]);
            }

            return linkedSites;
        }

        Vector2 ClampPointToMapBounds(Vector2 point)
        {
            float maxX = mapSize.x - 1f;
            float maxY = mapSize.y - 1f;
            return new Vector2(
                Mathf.Clamp(point.x, 0f, maxX),
                Mathf.Clamp(point.y, 0f, maxY));
        }

        Vector2 ClampPointToMapBoundsIfNearlyOnBoundary(Vector2 point)
        {
            // Circumcenters can land a tiny fraction outside the rectangle from float math.
            // Only snap those near misses so real outside circumcenters keep their Delaunay-dual position.
            float maxX = mapSize.x - 1f;
            float maxY = mapSize.y - 1f;
            float clampedX = point.x;
            float clampedY = point.y;

            if (Mathf.Abs(point.x) <= DistanceTieEpsilon)
                clampedX = 0f;
            else if (Mathf.Abs(point.x - maxX) <= DistanceTieEpsilon)
                clampedX = maxX;

            if (Mathf.Abs(point.y) <= DistanceTieEpsilon)
                clampedY = 0f;
            else if (Mathf.Abs(point.y - maxY) <= DistanceTieEpsilon)
                clampedY = maxY;

            return new Vector2(clampedX, clampedY);
        }

        bool IsPointOnMapBoundary(Vector2 point)
        {
            float maxX = mapSize.x - 1f;
            float maxY = mapSize.y - 1f;
            return Mathf.Abs(point.x) <= PolygonEpsilon ||
                   Mathf.Abs(point.y) <= PolygonEpsilon ||
                   Mathf.Abs(point.x - maxX) <= PolygonEpsilon ||
                   Mathf.Abs(point.y - maxY) <= PolygonEpsilon;
        }

        List<Vector2> CreateMapRectanglePolygon()
        {
            // The map rectangle is used for the one-site case and for assigning corner ownership.
            // Multi-site polygons are now built from Delaunay circumcenters and boundary support vertices.
            float maxX = mapSize.x - 1;
            float maxY = mapSize.y - 1;

            return new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(maxX, 0f),
                new Vector2(maxX, maxY),
                new Vector2(0f, maxY)
            };
        }

        bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygonVertices)
        {
            if (polygonVertices == null || polygonVertices.Count < 3)
                return false;

            bool isInside = false;
            int previousIndex = polygonVertices.Count - 1;

            for (int currentIndex = 0; currentIndex < polygonVertices.Count; currentIndex++)
            {
                Vector2 previousVertex = polygonVertices[previousIndex];
                Vector2 currentVertex = polygonVertices[currentIndex];

                if (IsPointOnSegment(point, previousVertex, currentVertex))
                    return true;

                bool crossesPointHeight = (currentVertex.y > point.y) != (previousVertex.y > point.y);
                if (crossesPointHeight)
                {
                    float xIntersection = (previousVertex.x - currentVertex.x) * (point.y - currentVertex.y) /
                                          (previousVertex.y - currentVertex.y) + currentVertex.x;

                    if (point.x < xIntersection)
                        isInside = !isInside;
                }

                previousIndex = currentIndex;
            }

            return isInside;
        }

        bool IsPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            Vector2 pointFromStart = point - segmentStart;
            float cross = segment.x * pointFromStart.y - segment.y * pointFromStart.x;
            if (Mathf.Abs(cross) > PolygonEpsilon)
                return false;

            float dot = Vector2.Dot(pointFromStart, segment);
            if (dot < -PolygonEpsilon)
                return false;

            return dot <= segment.sqrMagnitude + PolygonEpsilon;
        }

        void CacheClosestSiteOnNode(PathNode node, Vector2Int closestSiteGridPosition)
        {
            node.hasClosestVoronoiSite = true;
            node.closestVoronoiSiteGridPos = closestSiteGridPosition;
            node.closestVoronoiVersion = voronoiVersion;
        }

        bool IsGridPositionValid(Vector2Int gridPosition)
        {
            if (nodeGrid == null)
                return false;

            int gridWidth = nodeGrid.GetLength(0);
            int gridHeight = nodeGrid.GetLength(1);
            if (gridPosition.x < 0 || gridPosition.x >= gridWidth)
                return false;

            if (gridPosition.y < 0 || gridPosition.y >= gridHeight)
                return false;

            return gridPosition.x < mapSize.x && gridPosition.y < mapSize.y;
        }

        Vector2Int ClampGridPosition(Vector2Int gridPosition)
        {
            int gridWidth = nodeGrid != null ? nodeGrid.GetLength(0) : mapSize.x;
            int gridHeight = nodeGrid != null ? nodeGrid.GetLength(1) : mapSize.y;
            int maxX = Mathf.Max(0, Mathf.Min(mapSize.x, gridWidth) - 1);
            int maxY = Mathf.Max(0, Mathf.Min(mapSize.y, gridHeight) - 1);
            int clampedX = Mathf.Clamp(gridPosition.x, 0, maxX);
            int clampedY = Mathf.Clamp(gridPosition.y, 0, maxY);

            return new Vector2Int(clampedX, clampedY);
        }

        Vector2Int RoundToGridPosition(Vector2 gridPosition)
        {
            Vector2Int roundedGridPosition = new Vector2Int(
                Mathf.RoundToInt(gridPosition.x),
                Mathf.RoundToInt(gridPosition.y));

            return ClampGridPosition(roundedGridPosition);
        }

        Vector2 GetSiteGridPosition(VoronoiSite site) 
            => new(site.gridPos.x, site.gridPos.y);

        float GetSquaredEuclideanDistance(Vector2Int firstGridPosition, Vector2Int secondGridPosition)
        {
            // Squared distance keeps the same ordering as Euclidean distance.
            // Avoiding the square root makes repeated nearest-site sorting cheaper.
            Vector2Int difference = firstGridPosition - secondGridPosition;
            return difference.x * difference.x + difference.y * difference.y;
        }

        class BoundarySide
        {
            public bool isVertical;
            public float coordinate;
            public float minValue;
            public float maxValue;
            public Vector2 normal;
            public Vector2 axis;

            public BoundarySide(bool isVertical, float coordinate, float minValue, float maxValue,
                Vector2 normal, Vector2 axis)
            {
                this.isVertical = isVertical;
                this.coordinate = coordinate;
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.normal = normal;
                this.axis = axis;
            }
        }

        class DelaunayEdgeUse
        {
            public DelaunayEdge edge;
            public DelaunayTriangle triangle;
            public int useCount;

            public DelaunayEdgeUse(DelaunayEdge edge, DelaunayTriangle triangle)
            {
                this.edge = edge;
                this.triangle = triangle;
                useCount = 1;
            }
        }

        class DelaunayEdge
        {
            public Vector2 posA;
            public Vector2 posB;
            public VoronoiSite siteA;
            public VoronoiSite siteB;

            public DelaunayEdge(Vector2 posA, Vector2 posB, VoronoiSite siteA, VoronoiSite siteB)
            {
                this.posA = posA;
                this.posB = posB;
                this.siteA = siteA;
                this.siteB = siteB;
            }
            
            public bool IsSameEdge(DelaunayEdge other)
            {
                if (other == null)
                    return false;
                
                //If they are the same exact vector, return true
                if (AreSamePoint(posA, other.posA) && AreSamePoint(posB, other.posB))
                    return true;
                
                //If they are the opposite vector of each other, also return true
                //If not, they really are different edges
                return AreSamePoint(posA, other.posB) && AreSamePoint(posB, other.posA);
            }

            static bool AreSamePoint(Vector2 firstPoint, Vector2 secondPoint) 
                => (firstPoint - secondPoint).sqrMagnitude <= PolygonEpsilon;
        }

        class DelaunayTriangle
        {
            public Vector2 posA;
            public Vector2 posB;
            public Vector2 posC;
            public VoronoiSite siteA;
            public VoronoiSite siteB;
            public VoronoiSite siteC;

            public DelaunayTriangle(Vector2 posA, Vector2 posB, Vector2 posC,
                VoronoiSite siteA, VoronoiSite siteB, VoronoiSite siteC)
            {
                this.posA = posA;
                this.posB = posB;
                this.posC = posC;
                this.siteA = siteA;
                this.siteB = siteB;
                this.siteC = siteC;
            }

            public List<DelaunayEdge> GetEdges()
            {
                // Each Delaunay triangle has three site-to-site edges.
                // Those edges later tell us which Voronoi edges are shared and which ones are open.
                return new List<DelaunayEdge>
                {
                    new DelaunayEdge(posA, posB, siteA, siteB),
                    new DelaunayEdge(posB, posC, siteB, siteC),
                    new DelaunayEdge(posC, posA, siteC, siteA)
                };
            }

            public bool HasEdge(DelaunayEdge edge)
            {
                List<DelaunayEdge> edges = GetEdges();
                for (int i = 0; i < edges.Count; i++)
                {
                    if (edges[i].IsSameEdge(edge))
                        return true;
                }

                return false;
            }

            public bool HasSuperVertex()
            {
                return siteA == null || siteB == null || siteC == null;
            }

            public List<VoronoiSite> GetSites()
            {
                // The triangle stores both positions and optional site references.
                // Only real site references become Voronoi vertex parents.
                List<VoronoiSite> sites = new List<VoronoiSite>();
                if (siteA != null) sites.Add(siteA);
                if (siteB != null) sites.Add(siteB);
                if (siteC != null) sites.Add(siteC);
                return sites;
            }

            public bool TryGetSiteOutsideEdge(DelaunayEdge edge, out VoronoiSite outsideSite)
            {
                outsideSite = null;
                if (edge == null)
                    return false;

                // The outside site is the third corner of the triangle,
                // meaning the one that is not part of the tested Delaunay edge.
                if (siteA != edge.siteA && siteA != edge.siteB)
                    outsideSite = siteA;
                else if (siteB != edge.siteA && siteB != edge.siteB)
                    outsideSite = siteB;
                else if (siteC != edge.siteA && siteC != edge.siteB)
                    outsideSite = siteC;

                return outsideSite != null;
            }

            public bool IsTooSmall()
            {
                //If cross product is near 0, it means the vectors are almost parallel,
                //so the triangle does not work
                float cross = Cross(posB - posA, posC - posA);
                return cross * cross <= PolygonEpsilon;
            }

            public bool CircumcircleContains(Vector2 point)
            {
                Vector2 center = GetCircumcenter();
                double radiusSqr = (posA - center).sqrMagnitude;
                double pointDistanceSqr = (point - center).sqrMagnitude;
                return pointDistanceSqr <= radiusSqr + DistanceTieEpsilon;
            }

            /// <summary>
            /// Get the center of the circle formed by the 3 points
            /// If it fails, returns the center of the triangle
            /// </summary>
            /// <returns></returns>
            public Vector2 GetCircumcenter()
            {
                double aX = posA.x;
                double aY = posA.y;
                double bX = posB.x;
                double bY = posB.y;
                double cX = posC.x;
                double cY = posC.y;

                // We calculate the triangle area
                double triangleArea = 2.0 * (
                    aX * (bY - cY) +
                    bX * (cY - aY) +
                    cX * (aY - bY));

                //We can't divide by zero,
                //so if the triangle is flat, use avg as backup
                if (triangleArea >= -Mathf.Epsilon
                    && triangleArea <= Mathf.Epsilon)
                {
                    double averageX = (aX + bX + cX) / 3.0;
                    double averageY = (aY + bY + cY) / 3.0;
                    return new Vector2((float)averageX, (float)averageY);
                }

                //Get sqr distances from origin (sqr position)
                double aSqrDistFromOrigin = aX * aX + aY * aY;
                double bSqrDistFromOrigin = bX * bX + bY * bY;
                double cSqrDistFromOrigin = cX * cX + cY * cY;

                // Use perpendicular lines to get X
                double circumcenterX = 
                (aSqrDistFromOrigin * (bY - cY) +
                 bSqrDistFromOrigin * (cY - aY) +
                 cSqrDistFromOrigin * (aY - bY)) / triangleArea;

                //Use perpendicular lines to get Y
                double circumcenterY = (
                    aSqrDistFromOrigin * (cX - bX) +
                    bSqrDistFromOrigin * (aX - cX) +
                    cSqrDistFromOrigin * (bX - aX)) / triangleArea;

                return new Vector2((float)circumcenterX, (float)circumcenterY);
            }

            static float Cross(Vector2 firstVector, Vector2 secondVector)
            {
                return firstVector.x * secondVector.y - firstVector.y * secondVector.x;
            }
        }

    }
    internal class VoronoiOLD
    {
        const int MaxIncrementalDirtyCells = 256;
        const float MaxIncrementalDirtyRatio = 0.01f;
        const float MaxIncrementalTouchedRatio = 0.25f;

        public int[] regionsByNodeIndex;
        public int[] nearestCostByNodeIndex;
        public int version;
        public int poiVersion;

        public VoronoiOLD(int[] regionsByNodeIndex, int[] nearestCostByNodeIndex, int version, int poiVersion)
        {
            this.regionsByNodeIndex = regionsByNodeIndex;
            this.nearestCostByNodeIndex = nearestCostByNodeIndex;
            this.version = version;
            this.poiVersion = poiVersion;
        }

        internal VoronoiOLD DeepClone()
        {
            if (regionsByNodeIndex == null || nearestCostByNodeIndex == null)
                return null;

            return new VoronoiOLD(
                (int[])regionsByNodeIndex.Clone(),
                (int[])nearestCostByNodeIndex.Clone(),
                version,
                poiVersion);
        }

        internal static VoronoiOLD BuildTerrainRecalculationVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, List<NodeTerrainDelta> changes, VoronoiOLD baselineVoronoi,
            int[][] neighbourIndicesByNode, PathGrid grid)
        {
            if (ShouldUseIncremental(changes, walkableByNode.Length) && baselineVoronoi != null)
            {
                VoronoiOLD incremental = TryBuildIncrementalVoronoi(version, snapshotPoiVersion, sources,
                    walkableByNode, weightByNode, changes, baselineVoronoi, neighbourIndicesByNode, grid);

                if (incremental != null)
                    return incremental;
            }

            return BuildFullVoronoi(version, snapshotPoiVersion, sources, walkableByNode, weightByNode,
                neighbourIndicesByNode, grid);
        }

        internal static VoronoiOLD BuildFullVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, int[][] neighbourIndicesByNode, PathGrid grid)
        {
            int nodeCount = walkableByNode.Length;
            int[] regions = new int[nodeCount];
            int[] nearestCosts = new int[nodeCount];

            for (int i = 0; i < nodeCount; i++)
            {
                regions[i] = -1;
                nearestCosts[i] = int.MaxValue;
            }

            if (sources == null || sources.Count <= 0)
                return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);

            MinPriorityQueue queue = new MinPriorityQueue();
            for (int i = 0; i < sources.Count; i++)
            {
                PoiSource source = sources[i];
                if (source.nodeIndex < 0 || source.nodeIndex >= nodeCount)
                    continue;

                if (!walkableByNode[source.nodeIndex])
                    continue;

                queue.Enqueue(source.nodeIndex, source.poiId, 0);
            }

            while (queue.Count > 0)
            {
                RegionNode current = queue.Dequeue();

                if (!walkableByNode[current.nodeIndex])
                    continue;

                int bestDistance = nearestCosts[current.nodeIndex];
                int bestPoi = regions[current.nodeIndex];
                if (!IsBetterCandidate(current.distance, current.poiId, bestDistance, bestPoi))
                    continue;

                nearestCosts[current.nodeIndex] = current.distance;
                regions[current.nodeIndex] = current.poiId;

                int[] neighbours = neighbourIndicesByNode[current.nodeIndex];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int nextDistance = current.distance + GetMovementCostByIndex(current.nodeIndex, neighbour, weightByNode, grid);
                    if (!IsBetterCandidate(nextDistance, current.poiId, nearestCosts[neighbour], regions[neighbour]))
                        continue;

                    queue.Enqueue(neighbour, current.poiId, nextDistance);
                }
            }

            return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);
        }

        static bool ShouldUseIncremental(IReadOnlyList<NodeTerrainDelta> changes, int nodeCount)
        {
            if (changes == null || changes.Count == 0)
                return false;

            if (changes.Count > MaxIncrementalDirtyCells)
                return false;

            float dirtyRatio = (float)changes.Count / nodeCount;
            if (dirtyRatio > MaxIncrementalDirtyRatio)
                return false;

            for (int i = 0; i < changes.Count; i++)
            {
                NodeTerrainDelta change = changes[i];
                bool becameBlocked = change.oldWalkable && !change.newWalkable;
                bool increasedCost = change.oldWalkable && change.newWalkable && change.newWeight > change.oldWeight;
                if (becameBlocked || increasedCost)
                    return false;
            }

            return true;
        }

        static VoronoiOLD TryBuildIncrementalVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, IReadOnlyList<NodeTerrainDelta> changes,
            VoronoiOLD baselineVoronoi, int[][] neighbourIndicesByNode, PathGrid grid)
        {
            if (baselineVoronoi.regionsByNodeIndex == null || baselineVoronoi.nearestCostByNodeIndex == null)
                return null;

            int nodeCount = walkableByNode.Length;
            if (baselineVoronoi.regionsByNodeIndex.Length != nodeCount ||
                baselineVoronoi.nearestCostByNodeIndex.Length != nodeCount)
                return null;

            int[] regions = (int[])baselineVoronoi.regionsByNodeIndex.Clone();
            int[] nearestCosts = (int[])baselineVoronoi.nearestCostByNodeIndex.Clone();

            Dictionary<int, int> poiByNodeIndex = PoiSource.BuildLookupByNodeIndex(sources);
            HashSet<int> seedNodes = new HashSet<int>();

            for (int i = 0; i < changes.Count; i++)
            {
                int changedNode = changes[i].nodeIndex;
                seedNodes.Add(changedNode);

                int[] neighbours = neighbourIndicesByNode[changedNode];
                for (int j = 0; j < neighbours.Length; j++)
                    seedNodes.Add(neighbours[j]);
            }

            MinPriorityQueue queue = new MinPriorityQueue();
            int touchedNodes = 0;
            int touchedOverflowThreshold = (int)(nodeCount * MaxIncrementalTouchedRatio);
            if (touchedOverflowThreshold < 1)
                touchedOverflowThreshold = 1;

            foreach (int seedNode in seedNodes)
            {

                if (!walkableByNode[seedNode])
                {
                    if (regions[seedNode] != -1 || nearestCosts[seedNode] != int.MaxValue)
                    {
                        regions[seedNode] = -1;
                        nearestCosts[seedNode] = int.MaxValue;
                        touchedNodes++;
                        if (touchedNodes > touchedOverflowThreshold)
                            return null;
                    }
                    continue;
                }

                int bestRegion = regions[seedNode];
                int bestCost = nearestCosts[seedNode];

                if (poiByNodeIndex.TryGetValue(seedNode, out int poiOnNode))
                {
                    if (IsBetterCandidate(0, poiOnNode, bestCost, bestRegion))
                    {
                        bestCost = 0;
                        bestRegion = poiOnNode;
                    }
                }

                int[] neighbours = neighbourIndicesByNode[seedNode];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int neighbourRegion = regions[neighbour];
                    int neighbourCost = nearestCosts[neighbour];
                    if (neighbourRegion == -1 || neighbourCost == int.MaxValue)
                        continue;

                    int candidateCost = neighbourCost + GetMovementCostByIndex(neighbour, seedNode, weightByNode, grid);
                    if (IsBetterCandidate(candidateCost, neighbourRegion, bestCost, bestRegion))
                    {
                        bestCost = candidateCost;
                        bestRegion = neighbourRegion;
                    }
                }

                if (bestRegion != -1 && IsBetterCandidate(bestCost, bestRegion, nearestCosts[seedNode], regions[seedNode]))
                {
                    nearestCosts[seedNode] = bestCost;
                    regions[seedNode] = bestRegion;
                    touchedNodes++;
                    if (touchedNodes > touchedOverflowThreshold)
                        return null;

                    queue.Enqueue(seedNode, bestRegion, bestCost);
                }

                if (poiByNodeIndex.TryGetValue(seedNode, out poiOnNode))
                    queue.Enqueue(seedNode, poiOnNode, 0);
            }

            while (queue.Count > 0)
            {
                RegionNode current = queue.Dequeue();

                if (!walkableByNode[current.nodeIndex])
                    continue;

                int currentBestCost = nearestCosts[current.nodeIndex];
                int currentBestRegion = regions[current.nodeIndex];
                if (!IsBetterCandidate(current.distance, current.poiId, currentBestCost, currentBestRegion))
                    continue;

                nearestCosts[current.nodeIndex] = current.distance;
                regions[current.nodeIndex] = current.poiId;

                touchedNodes++;
                if (touchedNodes > touchedOverflowThreshold)
                    return null;

                int[] neighbours = neighbourIndicesByNode[current.nodeIndex];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int nextDistance = current.distance + GetMovementCostByIndex(current.nodeIndex, neighbour, weightByNode, grid);
                    if (!IsBetterCandidate(nextDistance, current.poiId, nearestCosts[neighbour], regions[neighbour]))
                        continue;

                    queue.Enqueue(neighbour, current.poiId, nextDistance);
                }
            }

            return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);
        }

        static bool IsBetterCandidate(int candidateCost, int candidatePoi, int currentCost, int currentPoi)
        {
            if (candidateCost < currentCost)
                return true;

            if (candidateCost > currentCost)
                return false;

            if (currentPoi == -1)
                return true;

            return candidatePoi < currentPoi;
        }

        static int GetMovementCostByIndex(int fromNodeIndex, int toNodeIndex, int[] weightByNode, PathGrid grid)
        {
            Vector2Int fromPos = grid.GetGridPositionFromIndex(fromNodeIndex);
            Vector2Int toPos = grid.GetGridPositionFromIndex(toNodeIndex);

            int dx = fromPos.x - toPos.x;
            if (dx < 0) dx = -dx;
            int dy = fromPos.y - toPos.y;
            if (dy < 0) dy = -dy;

            int sqrDistance = dx * dx + dy * dy;
            int travelCost = sqrDistance == 2 ? 14 : 10;
            return travelCost + weightByNode[toNodeIndex];
        }

        // -----------------------------------------------------------------------
        // BuildBowyerWatsonVoronoi
        // Builds a Voronoi diagram from a set of 2D seed positions using the
        // Bowyer-Watson algorithm (Delaunay triangulation dual) for adjacency and
        // edge lines, plus a multi-source BFS flood fill to assign each grid cell
        // to its nearest region.
        // -----------------------------------------------------------------------
        internal static BowyerWatsonResult BuildBowyerWatsonVoronoi(Vector2[] seeds, Vector2Int gridSize)
        {
            int regionCount = seeds != null ? seeds.Length : 0;
            List<(Vector2 from, Vector2 to)> edgeLines = new List<(Vector2, Vector2)>();
            int[][] adjacencyByRegion = new int[regionCount][];
            for (int i = 0; i < regionCount; i++)
                adjacencyByRegion[i] = new int[0];

            if (regionCount >= 1)
            {
                float w = gridSize.x, h = gridSize.y;

                // ── Phase 1: Create the super-triangle ──────────────────────────
                float margin = Mathf.Max(w, h) * 10f;
                Vector2 stA = new Vector2(w * 0.5f,  h + margin);
                Vector2 stB = new Vector2(-margin,   -margin);
                Vector2 stC = new Vector2(w + margin, -margin);

                List<Triangle> triangulation = new List<Triangle> { new Triangle(stA, stB, stC, -1, -2, -3) };

                // ── Phase 2: Insert each seed point one at a time ───────────────
                for (int i = 0; i < regionCount; i++)
                {
                    Vector2 p = seeds[i];

                    List<Triangle> bad = new List<Triangle>();
                    foreach (Triangle t in triangulation)
                        if (t.CircumcircleContains(p))
                            bad.Add(t);

                    List<(Vector2 a, Vector2 b, int idA, int idB)> polygon =
                        new List<(Vector2, Vector2, int, int)>();

                    foreach (Triangle t in bad)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            Vector2 ea = t.verts[e],  eb = t.verts[(e + 1) % 3];
                            int    eia = t.ids[e],    eib = t.ids[(e + 1) % 3];

                            bool shared = false;
                            foreach (Triangle other in bad)
                            {
                                if (ReferenceEquals(other, t)) continue;
                                for (int f = 0; f < 3; f++)
                                {
                                    Vector2 fa = other.verts[f], fb = other.verts[(f + 1) % 3];
                                    if (((ea - fa).sqrMagnitude < 1e-6f && (eb - fb).sqrMagnitude < 1e-6f) ||
                                        ((ea - fb).sqrMagnitude < 1e-6f && (eb - fa).sqrMagnitude < 1e-6f))
                                    { shared = true; break; }
                                }
                                if (shared) break;
                            }
                            if (!shared) polygon.Add((ea, eb, eia, eib));
                        }
                    }

                    foreach (Triangle t in bad) triangulation.Remove(t);
                    foreach ((Vector2 a, Vector2 b, int ia, int ib) in polygon)
                        triangulation.Add(new Triangle(a, b, p, ia, ib, i));
                }

                // ── Phase 3: Clean up the super-triangle ────────────────────────
                triangulation.RemoveAll(t => t.IsSuperTriangle);

                // ── Phase 4: Build the region adjacency graph ───────────────────
                HashSet<int>[] adjacencySets = new HashSet<int>[regionCount];
                for (int i = 0; i < regionCount; i++)
                    adjacencySets[i] = new HashSet<int>();

                foreach (Triangle tri in triangulation)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int idA = tri.ids[e], idB = tri.ids[(e + 1) % 3];
                        if (idA >= 0 && idB >= 0 && idA != idB)
                        {
                            adjacencySets[idA].Add(idB);
                            adjacencySets[idB].Add(idA);
                        }
                    }
                }

                for (int i = 0; i < regionCount; i++)
                {
                    adjacencyByRegion[i] = new int[adjacencySets[i].Count];
                    adjacencySets[i].CopyTo(adjacencyByRegion[i]);
                }

                // ── Phase 5: Derive Voronoi edges from the Delaunay dual ─────────
                HashSet<string> emitted = new HashSet<string>();
                for (int i = 0; i < triangulation.Count; i++)
                {
                    Triangle triA = triangulation[i];
                    for (int e = 0; e < 3; e++)
                    {
                        Vector2 ea = triA.verts[e], eb = triA.verts[(e + 1) % 3];
                        int neighborIdx = -1;

                        for (int j = 0; j < triangulation.Count; j++)
                        {
                            if (j == i) continue;
                            Triangle triB = triangulation[j];
                            bool hasA = false, hasB = false;
                            for (int v = 0; v < 3; v++)
                            {
                                if ((triB.verts[v] - ea).sqrMagnitude < 1e-6f) hasA = true;
                                if ((triB.verts[v] - eb).sqrMagnitude < 1e-6f) hasB = true;
                            }
                            if (hasA && hasB) { neighborIdx = j; break; }
                        }

                        if (neighborIdx >= 0)
                        {
                            string key = Mathf.Min(i, neighborIdx) + "," + Mathf.Max(i, neighborIdx);
                            if (!emitted.Contains(key))
                            {
                                emitted.Add(key);
                                edgeLines.Add((triA.Circumcenter(), triangulation[neighborIdx].Circumcenter()));
                            }
                        }
                        else
                        {
                            Vector2 edgeMid = (ea + eb) * 0.5f;
                            Vector2 edgeDir = (eb - ea).normalized;
                            Vector2 perp    = new Vector2(-edgeDir.y, edgeDir.x);
                            int     oppIdx  = (e + 2) % 3;
                            Vector2 inward  = (triA.verts[oppIdx] - edgeMid).normalized;
                            if (Vector2.Dot(perp, inward) > 0f) perp = -perp;

                            Vector2 cc     = triA.Circumcenter();
                            Vector2 rayEnd = cc + perp * (Mathf.Max(w, h) * 4f);
                            if (ClipSegmentToRect(ref cc, ref rayEnd, new Rect(0f, 0f, w, h)))
                                edgeLines.Add((cc, rayEnd));
                        }
                    }
                }
            }

            // ── BFS Flood Fill ──────────────────────────────────────────────────
            int[,] regionMap = new int[gridSize.x, gridSize.y];
            for (int x = 0; x < gridSize.x; x++)
                for (int y = 0; y < gridSize.y; y++)
                    regionMap[x, y] = -1;

            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            for (int i = 0; i < regionCount; i++)
            {
                int sx = Mathf.Clamp(Mathf.RoundToInt(seeds[i].x), 0, gridSize.x - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(seeds[i].y), 0, gridSize.y - 1);
                if (regionMap[sx, sy] != -1) continue;
                regionMap[sx, sy] = i;
                frontier.Enqueue(new Vector2Int(sx, sy));
            }

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (frontier.Count > 0)
            {
                Vector2Int cell  = frontier.Dequeue();
                int        owner = regionMap[cell.x, cell.y];
                foreach (Vector2Int d in dirs)
                {
                    Vector2Int nb = cell + d;
                    if (nb.x < 0 || nb.x >= gridSize.x || nb.y < 0 || nb.y >= gridSize.y) continue;
                    if (regionMap[nb.x, nb.y] != -1) continue;
                    regionMap[nb.x, nb.y] = owner;
                    frontier.Enqueue(nb);
                }
            }

            return new BowyerWatsonResult { regionMap = regionMap, edgeLines = edgeLines, adjacencyByRegion = adjacencyByRegion };
        }

        static bool ClipSegmentToRect(ref Vector2 p0, ref Vector2 p1, Rect rect)
        {
            float dx = p1.x - p0.x, dy = p1.y - p0.y;
            float[] pArr = { -dx,  dx, -dy,  dy };
            float[] qArr = { p0.x - rect.xMin, rect.xMax - p0.x,
                             p0.y - rect.yMin, rect.yMax - p0.y };
            float t0 = 0f, t1 = 1f;

            for (int k = 0; k < 4; k++)
            {
                if (Mathf.Abs(pArr[k]) < 1e-10f)
                {
                    if (qArr[k] < 0f) return false;
                    continue;
                }
                float t = qArr[k] / pArr[k];
                if (pArr[k] < 0f) t0 = Mathf.Max(t0, t);
                else              t1 = Mathf.Min(t1, t);
            }

            if (t0 > t1) return false;
            p1 = p0 + t1 * new Vector2(dx, dy);
            p0 = p0 + t0 * new Vector2(dx, dy);
            return true;
        }

        class Triangle
        {
            public readonly Vector2[] verts = new Vector2[3];
            public readonly int[]     ids   = new int[3];

            public Triangle(Vector2 a, Vector2 b, Vector2 c, int ia, int ib, int ic)
            {
                verts[0] = a; verts[1] = b; verts[2] = c;
                ids[0]   = ia; ids[1]  = ib; ids[2]  = ic;
            }

            public bool IsSuperTriangle => ids[0] < 0 || ids[1] < 0 || ids[2] < 0;

            public Vector2 Circumcenter()
            {
                double ax = verts[0].x, ay = verts[0].y;
                double bx = verts[1].x, by = verts[1].y;
                double cx = verts[2].x, cy = verts[2].y;

                double D = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                if (System.Math.Abs(D) < 1e-10)
                    return new Vector2((float)((ax + bx + cx) / 3.0), (float)((ay + by + cy) / 3.0));

                double a2 = ax * ax + ay * ay;
                double b2 = bx * bx + by * by;
                double c2 = cx * cx + cy * cy;

                double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / D;
                double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / D;

                return new Vector2((float)ux, (float)uy);
            }

            public bool CircumcircleContains(Vector2 p)
            {
                Vector2 cc = Circumcenter();
                double r2 = (double)(verts[0].x - cc.x) * (verts[0].x - cc.x)
                          + (double)(verts[0].y - cc.y) * (verts[0].y - cc.y);
                double d2 = (double)(p.x - cc.x) * (p.x - cc.x)
                          + (double)(p.y - cc.y) * (p.y - cc.y);
                return d2 <= r2 + 1e-6;
            }

            public bool SharesEdgeWith(Triangle other)
            {
                int count = 0;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        if ((verts[i] - other.verts[j]).sqrMagnitude < 1e-6f) count++;
                return count == 2;
            }
        }
    }

    internal class BowyerWatsonResult
    {
        public int[,] regionMap;
        public List<(Vector2 from, Vector2 to)> edgeLines;
        public int[][] adjacencyByRegion;
    }
}
