// Milestone 4: a copy of Elden Ring's collision around the player, built inside AoTTG2 so its
// hero walks on Elden Ring's ground and its hooks catch Elden Ring's walls.
//
// While linked, AoTTG2's own map colliders are switched off. Elden Ring's world is sampled in
// 16 x 16 m tiles (a column every metre) with three rays per column, relative to the height the
// player was at when the tile was sampled (yRef):
//   floor : down from yRef + 2.5 m        the ground under someone standing at that level
//   top   : down from yRef + 60 m         the highest surface in the column
//   ceil  : up from yRef + 0.3 m          the underside of anything above the player's level
// Each tile becomes one mesh collider: a ground surface where neighbouring floors are close in
// height, plus a 1 x 1 m column box wherever something rises above the player's level (walls,
// roofs, hills, trees). 2.5D only: good for ground and walls, approximate for overhangs.
// Tiles are kept within ~48 m and re-sampled when the player's height changes a lot.
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Aoer
{
    internal static class TerrainMirror
    {
        private const int TileSize = 16;               // metres, one column per metre
        private const int Edge = TileSize + 1;         // 17 x 17 columns per tile
        private const int RaysPerTile = Edge * Edge * 3;
        private const int TilesPerBatch = 9;
        private const int KeepRadius = 3;              // tiles around the player to keep sampled
        private const int DropRadius = 5;
        private const float ResampleHeight = 5f;
        private const float Reach = 60f;

        private sealed class Tile
        {
            public int TX, TZ;
            public float YRef;
            public float[,] Floor;
            public GameObject Go;
        }

        private static readonly Dictionary<long, Tile> _tiles = new();
        private static readonly List<(int tx, int tz, float yRef)> _batch = new();
        private static readonly Ray3[] _rays = new Ray3[Rays.MaxRays];
        private static readonly RayHit[] _hits = new RayHit[Rays.MaxRays];
        private static readonly List<Collider> _disabled = new();

        private static GameObject _root;
        private static Vector3 _offset;                 // unity = er + _offset (on _root)
        private static int _layer;
        private static int _heroInclude, _heroExclude, _underFeet = -1;
        private static float _batchStart;
        public static int Batches, RaysCast, FloorHits, TopHits, Triangles, SolidColumns;
        public static float LastBatchMs;
        public static bool Active { get; private set; }

        private static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

        public static void Begin()
        {
            End();
            _root = new GameObject("AoER terrain mirror");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            SetOffset(Vector3.zero);
            Batches = RaysCast = 0;
            FloorHits = TopHits = Triangles = SolidColumns = 0;
            Active = true;
        }

        /// <summary>True once the 3 x 3 tiles around the given Elden Ring point are built.</summary>
        public static bool ReadyAround(float erX, float erZ)
        {
            int cx = (int)Math.Floor(erX / TileSize), cz = (int)Math.Floor(erZ / TileSize);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (!_tiles.ContainsKey(Key(cx + dx, cz + dz))) return false;
            return true;
        }

        /// <summary>Called every frame while linked. erPos: player position in Elden Ring; vel: m/s.</summary>
        public static void Update(Vector3 erPos, Vector3 vel)
        {
            if (!Active) return;
            if (Rays.Busy)
            {
                if (Rays.TryCollect(_hits)) BuildBatch();
                return;
            }
            // Pick the tiles most needed next, looking ahead along the velocity.
            int cx = (int)Math.Floor(erPos.x / TileSize), cz = (int)Math.Floor(erPos.z / TileSize);
            Vector3 lead = erPos + vel * 1.0f;
            var wanted = new List<(float score, int tx, int tz)>();
            for (int dx = -KeepRadius; dx <= KeepRadius; dx++)
                for (int dz = -KeepRadius; dz <= KeepRadius; dz++)
                {
                    int tx = cx + dx, tz = cz + dz;
                    if (_tiles.TryGetValue(Key(tx, tz), out var t) && Math.Abs(t.YRef - erPos.y) < ResampleHeight) continue;
                    float mx = (tx + 0.5f) * TileSize - lead.x, mz = (tz + 0.5f) * TileSize - lead.z;
                    wanted.Add((mx * mx + mz * mz, tx, tz));
                }
            DropFar(cx, cz);
            if (wanted.Count == 0) return;
            wanted.Sort((a, b) => a.score.CompareTo(b.score));
            _batch.Clear();
            int n = 0;
            foreach (var w in wanted)
            {
                if (_batch.Count >= TilesPerBatch) break;
                _batch.Add((w.tx, w.tz, erPos.y));
                n = AddTileRays(w.tx, w.tz, erPos.y, n);
            }
            if (Rays.Submit(_rays, n))
            {
                _batchStart = Time.realtimeSinceStartup;
                RaysCast += n;
            }
        }

        private static int AddTileRays(int tx, int tz, float yRef, int n)
        {
            float x0 = tx * TileSize, z0 = tz * TileSize;
            for (int i = 0; i < Edge; i++)
                for (int j = 0; j < Edge; j++)
                {
                    float x = x0 + i, z = z0 + j;
                    _rays[n++] = new Ray3(x, yRef + 2.5f, z, x, yRef - Reach, z);  // floor
                    _rays[n++] = new Ray3(x, yRef + Reach, z, x, yRef - Reach, z); // top
                    _rays[n++] = new Ray3(x, yRef + 0.3f, z, x, yRef + Reach, z);  // ceiling
                }
            return n;
        }

        private static void BuildBatch()
        {
            LastBatchMs = (Time.realtimeSinceStartup - _batchStart) * 1000f;
            Batches++;
            int n = 0;
            foreach (var (tx, tz, yRef) in _batch)
            {
                BuildTile(tx, tz, yRef, n);
                n += RaysPerTile;
            }
            _batch.Clear();
        }

        private static void BuildTile(int tx, int tz, float yRef, int first)
        {
            var floor = new float[Edge, Edge];
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float x0 = tx * TileSize, z0 = tz * TileSize;
            for (int i = 0; i < Edge; i++)
                for (int j = 0; j < Edge; j++)
                {
                    int k = first + (i * Edge + j) * 3;
                    RayHit f = _hits[k], top = _hits[k + 1], ceil = _hits[k + 2];
                    // Is there open ground at the player's level in this column? The floor ray started
                    // at yRef + 2.5 and found a surface close below the player's level.
                    bool openHere = f.Hit && f.Y >= yRef - 3f;
                    if (top.Hit && top.Y > yRef + 2.5f)
                    {
                        // Something rises above the player's level. Only call it an overhang (roof,
                        // arch, cave ceiling) when there is open ground under it with real headroom;
                        // otherwise the column is solid from far below up to its top (wall, cliff, the
                        // side of a pit). Milestone 4 test: from inside a pit, the "ceiling" ray started
                        // inside the hill and hit the hill's top from below, which made a hollow wall.
                        bool overhang = openHere && ceil.Hit && ceil.Y > f.Y + 1.8f && ceil.Y < top.Y - 0.05f;
                        if (overhang)
                        {
                            AddBox(verts, tris, x0 + i, z0 + j, ceil.Y, top.Y);
                            floor[i, j] = f.Y;
                        }
                        else
                        {
                            AddBox(verts, tris, x0 + i, z0 + j, yRef - Reach, top.Y);
                            floor[i, j] = top.Y;  // the walkable top, and what the rescue net lifts to
                            SolidColumns++;
                        }
                    }
                    else
                    {
                        // Nothing above the player's level: the floor, or (if the floor ray started inside
                        // a rock and missed) the top surface.
                        floor[i, j] = f.Hit ? f.Y : top.Hit ? top.Y : float.NaN;
                    }
                    if (f.Hit) FloorHits++;
                    if (top.Hit) TopHits++;
                }
            // Ground surface between every four neighbouring floor samples, steep or not: a hole is
            // worse than a steep ramp (milestone 4 test: the hero fell through a rocky slope).
            for (int i = 0; i < TileSize; i++)
                for (int j = 0; j < TileSize; j++)
                {
                    float a = floor[i, j], b = floor[i + 1, j], c = floor[i, j + 1], d = floor[i + 1, j + 1];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d)) continue;
                    int v = verts.Count;
                    verts.Add(U(x0 + i, a, z0 + j)); verts.Add(U(x0 + i + 1, b, z0 + j));
                    verts.Add(U(x0 + i, c, z0 + j + 1)); verts.Add(U(x0 + i + 1, d, z0 + j + 1));
                    // Clockwise seen from above = facing up in Unity's left-handed space.
                    tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                    tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
                }

            long key = Key(tx, tz);
            if (_tiles.TryGetValue(key, out var old) && old.Go != null) UnityEngine.Object.Destroy(old.Go);
            var tile = new Tile { TX = tx, TZ = tz, YRef = yRef, Floor = floor };
            Triangles += tris.Count / 3;
            if (tris.Count > 0)
            {
                var mesh = new Mesh();
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = new Il2CppStructArray<Vector3>(verts.ToArray());
                mesh.triangles = new Il2CppStructArray<int>(tris.ToArray());
                mesh.RecalculateBounds();
                var go = new GameObject($"AoER tile {tx},{tz}");
                go.transform.SetParent(_root.transform, false);
                go.layer = _layer;
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                tile.Go = go;
            }
            _tiles[key] = tile;
        }

        // Tile vertices are in Elden Ring coordinates; the root object carries the offset.
        private static Vector3 U(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>unity = er + offset. Moves every tile at once.</summary>
        public static void SetOffset(Vector3 offset)
        {
            _offset = offset;
            if (_root != null) _root.transform.position = offset;
        }

        // A closed 1 x 1 m box around column (x, z), faces pointing outwards.
        private static void AddBox(List<Vector3> v, List<int> t, float x, float z, float y0, float y1)
        {
            int b = v.Count;
            float a = x - 0.5f, c = x + 0.5f, d = z - 0.5f, e = z + 0.5f;
            v.Add(U(a, y0, d)); v.Add(U(c, y0, d)); v.Add(U(c, y0, e)); v.Add(U(a, y0, e));
            v.Add(U(a, y1, d)); v.Add(U(c, y1, d)); v.Add(U(c, y1, e)); v.Add(U(a, y1, e));
            int[] q =
            {
                4, 7, 6, 4, 6, 5,   // top
                0, 1, 2, 0, 2, 3,   // bottom
                0, 4, 5, 0, 5, 1,   // -z side
                2, 6, 7, 2, 7, 3,   // +z side
                3, 7, 4, 3, 4, 0,   // -x side
                1, 5, 6, 1, 6, 2,   // +x side
            };
            foreach (int i in q) t.Add(b + i);
        }

        private static void DropFar(int cx, int cz)
        {
            List<long> drop = null;
            foreach (var kv in _tiles)
                if (Math.Abs(kv.Value.TX - cx) > DropRadius || Math.Abs(kv.Value.TZ - cz) > DropRadius)
                    (drop ??= new List<long>()).Add(kv.Key);
            if (drop == null) return;
            foreach (long k in drop)
            {
                if (_tiles[k].Go != null) UnityEngine.Object.Destroy(_tiles[k].Go);
                _tiles.Remove(k);
            }
        }

        public static int TileCount => _tiles.Count;
        public static int Layer => _layer;

        /// <summary>
        /// Height of the copied ground at the nearest column to an Elden Ring point, if a tile with a
        /// floor there exists. Used to catch the hero if it ever ends up under the copy.
        /// </summary>
        public static bool FloorAt(float erX, float erZ, out float y)
        {
            y = float.NaN;
            int tx = (int)Math.Floor(erX / TileSize), tz = (int)Math.Floor(erZ / TileSize);
            if (!_tiles.TryGetValue(Key(tx, tz), out var t) || t.Floor == null) return false;
            int i = (int)Math.Round(erX - tx * TileSize), j = (int)Math.Round(erZ - tz * TileSize);
            if (i < 0 || j < 0 || i >= Edge || j >= Edge) return false;
            y = t.Floor[i, j];
            return !float.IsNaN(y);
        }

        /// <summary>Switches off AoTTG2's own map collision (everything not part of a character).</summary>
        public static void DisableAoTTG2Map()
        {
            _disabled.Clear();
            var layers = new Dictionary<int, int>();
            var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Collider>());
            foreach (var o in all)
            {
                var col = o.TryCast<Collider>();
                if (col == null || !col.enabled) continue;
                if (col.attachedRigidbody != null) continue;  // characters and moving objects
                if (col.GetComponentInParent<Characters.BaseCharacter>() != null) continue;
                if (_root != null && col.transform.IsChildOf(_root.transform)) continue;
                if (!col.isTrigger) layers[col.gameObject.layer] = layers.TryGetValue(col.gameObject.layer, out int c) ? c + 1 : 1;
                col.enabled = false;
                _disabled.Add(col);
            }
            var summary = new List<string>();
            foreach (var kv in layers) summary.Add($"{LayerMask.LayerToName(kv.Key)}({kv.Key}) x{kv.Value}");
            Plugin.L.LogMessage($"terrain: switched off {_disabled.Count} AoTTG2 map colliders; layers {string.Join(", ", summary)}; " +
                                $"Elden Ring collision uses layer {LayerMask.LayerToName(_layer)} ({_layer})");
        }

        /// <summary>
        /// Picks the layer for Elden Ring's collision: one AoTTG2's hero collides with and, when the
        /// hero's hook mask is known, one its hooks can catch. Map layers are preferred. Must be
        /// called before any tile is built (Begin).
        /// </summary>
        public static void ChooseLayer(Component hero)
        {
            int heroLayer = hero.gameObject.layer;
            int hookMask = HookMask(hero);
            DescribeHeroColliders(hero, out int include, out int exclude);
            _heroInclude = include;
            _heroExclude = exclude;
            int under = LayerUnderFeet(hero);
            _underFeet = under;
            var order = new List<int>();
            if (under >= 0) order.Add(under);
            order.AddRange(new[]
            {
                Utility.PhysicsLayer.MapObjectCharacters, Utility.PhysicsLayer.MapObjectAll,
                Utility.PhysicsLayer.MapObjectHumans, Utility.PhysicsLayer.MapObjectEntities,
                Utility.PhysicsLayer.MapObjectMapObjects,
            });
            for (int l = 0; l < 32; l++) order.Add(l);
            var candidates = new List<string>();
            int chosen = -1;
            var seen = new HashSet<int>();
            foreach (int l in order)
            {
                if (!seen.Add(l)) continue;
                bool collides = Collides(heroLayer, l, include, exclude);
                bool hookable = hookMask == 0 || (hookMask & (1 << l)) != 0;
                if (seen.Count <= 6) candidates.Add($"{LayerMask.LayerToName(l)}({l}) collide={collides} hook={hookable}");
                if (chosen < 0 && collides && hookable) chosen = l;
            }
            _layer = chosen >= 0 ? chosen : (under >= 0 ? under : Utility.PhysicsLayer.MapObjectAll);
            Plugin.L.LogMessage($"terrain: hero layer {LayerMask.LayerToName(heroLayer)} ({heroLayer}), standing on layer " +
                                $"{(under >= 0 ? $"{LayerMask.LayerToName(under)} ({under})" : "none found")}, hook mask {hookMask:x8}, " +
                                $"collider include {include:x8} exclude {exclude:x8}; {string.Join(", ", candidates)} -> using {LayerMask.LayerToName(_layer)} ({_layer})");
        }

        // Unity: two objects collide when the layer matrix allows it or a per-collider/rigidbody
        // include override adds the layer, unless an exclude override removes it.
        private static bool Collides(int heroLayer, int l, int include, int exclude)
        {
            bool matrix = !Physics.GetIgnoreLayerCollision(heroLayer, l);
            return (matrix || (include & (1 << l)) != 0) && (exclude & (1 << l)) == 0;
        }

        private static void DescribeHeroColliders(Component hero, out int include, out int exclude)
        {
            include = 0; exclude = 0;
            var parts = new List<string>();
            try
            {
                var rb = hero.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    include |= rb.includeLayers.value;
                    exclude |= rb.excludeLayers.value;
                    parts.Add($"rigidbody include {rb.includeLayers.value:x8} exclude {rb.excludeLayers.value:x8}");
                }
                foreach (var c in hero.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || (rb != null && c.attachedRigidbody != rb)) continue;
                    include |= c.includeLayers.value;
                    exclude |= c.excludeLayers.value;
                    parts.Add($"{c.GetIl2CppType().Name} '{c.gameObject.name}' layer {c.gameObject.layer} include {c.includeLayers.value:x8} exclude {c.excludeLayers.value:x8}");
                }
            }
            catch (Exception e)
            {
                parts.Add("unreadable: " + e.Message);
            }
            Plugin.L.LogMessage("terrain: hero physics: " + string.Join("; ", parts));
        }

        /// <summary>Layer of the first solid map object straight under the hero, or -1.</summary>
        private static int LayerUnderFeet(Component hero)
        {
            var hits = Physics.RaycastAll(hero.transform.position + Vector3.up * 1f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int layer = -1;
            foreach (var h in hits)
            {
                var c = h.collider;
                if (c == null || c.attachedRigidbody != null) continue;  // the hero itself, other characters
                if (h.distance < best) { best = h.distance; layer = c.gameObject.layer; }
            }
            return layer;
        }

        private static int HookMask(Component hero)
        {
            try
            {
                return Characters.Hook.HookMask.value;  // static: shared by every hook
            }
            catch (Exception e)
            {
                Plugin.L.LogDebug("terrain: hook mask unreadable: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// Checks there is mirrored ground under a point (unity space): a ray straight down that must
        /// hit one of our tiles. Logs what it found either way.
        /// </summary>
        public static bool GroundBelow(Vector3 unityPos, int heroLayer, out string report)
        {
            // AoTTG2 keeps its hero up with its own ground checks against map layers, not with Unity's
            // layer matrix (the matrix says Human ignores every map layer, yet the hero stands on
            // trees). So the test is: the copy uses the same layer as what the hero stands on now,
            // or a layer Unity says it collides with, and a copied tile really is under its feet.
            bool sameAsFeet = _underFeet >= 0 && _layer == _underFeet;
            bool collides = Collides(heroLayer, _layer, _heroInclude, _heroExclude);
            float tileBelow = float.NaN;
            var hits = Physics.RaycastAll(unityPos + Vector3.up * 2f, Vector3.down, 12f, 1 << _layer, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (_root != null && h.collider.transform.IsChildOf(_root.transform) && (float.IsNaN(tileBelow) || h.distance - 2f < tileBelow))
                    tileBelow = h.distance - 2f;
            bool found = !float.IsNaN(tileBelow) && tileBelow > -1.5f && tileBelow < 3f;
            report = $"copy on {LayerMask.LayerToName(_layer)} ({_layer}): same layer as the hero's footing: {sameAsFeet}, " +
                     $"Unity layer matrix collides: {collides}; " +
                     (float.IsNaN(tileBelow) ? "no copied tile under the hero" : $"copied tile {tileBelow:F2} m below the hero");
            return found && (sameAsFeet || collides);
        }

        public static void End()
        {
            Active = false;
            Rays.Abandon();
            foreach (var t in _tiles.Values) if (t.Go != null) UnityEngine.Object.Destroy(t.Go);
            _tiles.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            int restored = 0;
            foreach (var c in _disabled)
            {
                if (c == null) continue;
                c.enabled = true;
                restored++;
            }
            if (_disabled.Count > 0) Plugin.L.LogMessage($"terrain: AoTTG2 map collision restored ({restored} colliders)");
            _disabled.Clear();
        }
    }
}
