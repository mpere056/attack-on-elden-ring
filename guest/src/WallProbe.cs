// Walls around the player, seen directly. About 10 times a second, rays go out level from the
// player in 48 directions at knee, chest and head height (0.7, 1.3, 1.9 m), up to 20 m. Every hit
// becomes a thin box collider standing on the surface, facing back along the ray, sized to cover
// the gap to the neighbouring rays at that distance. The column copy (TerrainMirror) can't describe
// caves and corridors well; these panels make whatever the player is actually next to solid.
// Wall or slope? Each direction has rays at three heights. If the next ray up stops at about the same
// distance, the surface is (near) vertical: a wall. If it goes noticeably further, it is a slope you
// can walk up, and it is skipped (the column copy already has it as ground). The top ray is compared
// with the one below it; with nothing below it, it is a beam or an overhang edge: a wall.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aoer
{
    internal static class WallProbe
    {
        private const int Directions = 48;
        private static readonly float[] Heights = { 0.7f, 1.3f, 1.9f };
        private const float Range = 20f;
        private const float Interval = 0.1f;
        private const float Thickness = 0.25f;
        private const float WallSlack = 0.35f;        // < 0.35 m further at 0.6 m higher = steeper than ~60 degrees

        private static readonly List<GameObject> _pool = new();
        private static int _used;
        private static float _next;
        private static Vector3 _origin;
        private static int _count;
        public static int Panels { get; private set; }

        public static void Begin()
        {
            _next = 0;
            _used = 0;
            Panels = 0;
        }

        public static bool Due() => Time.unscaledTime >= _next && TerrainMirror.Root != null;

        public static bool Submit(Vector3 er, Ray3[] rays)
        {
            _origin = er;
            int n = 0;
            foreach (float h in Heights)
                for (int d = 0; d < Directions; d++)
                {
                    float a = d * (2f * Mathf.PI / Directions);
                    float sx = er.x, sy = er.y + h, sz = er.z;
                    rays[n++] = new Ray3(sx, sy, sz, sx + Mathf.Sin(a) * Range, sy, sz + Mathf.Cos(a) * Range);
                }
            _count = n;
            _next = Time.unscaledTime + Interval;
            return Rays.Submit(rays, n);
        }

        public static void Collect(RayHit[] hits)
        {
            Transform root = TerrainMirror.Root;
            if (root == null) return;
            _used = 0;
            int levels = Heights.Length;
            var dist = new float[levels, Directions];
            for (int l = 0, k = 0; l < levels; l++)
                for (int d = 0; d < Directions; d++, k++)
                    dist[l, d] = hits[k].Hit ? new Vector2(hits[k].X - _origin.x, hits[k].Z - _origin.z).magnitude : float.NaN;
            float spacing = 2f * Mathf.Tan(Mathf.PI / Directions);  // panel width per metre of distance
            for (int l = 0, k = 0; l < levels; l++)
                for (int d = 0; d < Directions; d++, k++)
                {
                    float me = dist[l, d];
                    if (float.IsNaN(me)) continue;
                    bool wall;
                    if (l + 1 < levels)
                    {
                        float up = dist[l + 1, d];
                        wall = !float.IsNaN(up) && up - me < WallSlack;
                    }
                    else
                    {
                        float below = dist[l - 1, d];
                        wall = float.IsNaN(below) || me - below < WallSlack;
                    }
                    if (!wall) continue;
                    RayHit r = hits[k];
                    float a = d * (2f * Mathf.PI / Directions);
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    float width = Mathf.Max(0.4f, me * spacing + 0.2f);
                    GameObject go = Panel(root);
                    go.transform.localPosition = new Vector3(r.X, r.Y, r.Z) + dir * (Thickness * 0.5f);
                    go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                    go.transform.localScale = new Vector3(width, 0.75f, Thickness);
                }
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i] != null && _pool[i].activeSelf) _pool[i].SetActive(false);
            Panels = _used;
        }

        private static GameObject Panel(Transform root)
        {
            if (_used < _pool.Count && _pool[_used] != null)
            {
                var g = _pool[_used++];
                if (!g.activeSelf) g.SetActive(true);
                return g;
            }
            var go = new GameObject("AoER wall");
            go.transform.SetParent(root, false);
            go.layer = TerrainMirror.Layer;
            go.AddComponent<BoxCollider>();  // unit box, scaled by the transform
            if (_used < _pool.Count) _pool[_used] = go; else _pool.Add(go);
            _used++;
            return go;
        }

        public static void End()
        {
            // The panels are children of the terrain root, which TerrainMirror destroys.
            _pool.Clear();
            _used = 0;
            Panels = 0;
        }
    }
}
