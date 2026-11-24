using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Trait du GPS sur la carte : une ligne brisée dessinée dans l'espace local de son parent (l'image de
    /// la ville), donc elle tourne et se déplace avec la mini-carte sans calcul supplémentaire. Le maillage
    /// n'est reconstruit que lorsque le chemin ou l'échelle change.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapPathLine : MaskableGraphic
    {
        private readonly List<Vector2> _points = new List<Vector2>(64);
        private float _width = 4f;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>Remplace le tracé. Les points sont en pixels, dans l'espace local du parent.</summary>
        public void SetPoints(IReadOnlyList<Vector2> points, float width)
        {
            _points.Clear();
            for (int i = 0; i < points.Count; i++)
                _points.Add(points[i]);
            _width = width;
            SetVerticesDirty();
        }

        public void Clear()
        {
            if (_points.Count == 0)
                return;
            _points.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float half = _width * 0.5f;
            for (int i = 0; i + 1 < _points.Count; i++)
            {
                Vector2 a = _points[i];
                Vector2 b = _points[i + 1];
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 0.01f)
                    continue;
                dir.Normalize();
                Vector2 normal = new Vector2(-dir.y, dir.x) * half;
                // Le segment déborde d'une demi-largeur à chaque bout : les coudes restent pleins.
                a -= dir * half;
                b += dir * half;

                int start = vh.currentVertCount;
                vh.AddVert(a - normal, color, Vector4.zero);
                vh.AddVert(a + normal, color, Vector4.zero);
                vh.AddVert(b + normal, color, Vector4.zero);
                vh.AddVert(b - normal, color, Vector4.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
