using UnityEngine;
using UnityEngine.UI;

namespace IvoryState.Client.UI
{
    /// <summary>Icône de logement sur la carte : murs et toit de la même couleur (libre, pris, à moi).</summary>
    [DisallowMultipleComponent]
    public sealed class MapHouseTint : MonoBehaviour
    {
        public Image Roof;

        public void Apply(Color color)
        {
            if (TryGetComponent(out Graphic body))
                body.color = color;
            if (Roof != null)
                Roof.color = color;
        }
    }
}
