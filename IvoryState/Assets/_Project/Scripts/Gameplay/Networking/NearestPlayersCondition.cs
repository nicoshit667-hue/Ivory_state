using FishNet.Connection;
using FishNet.Observing;
using UnityEngine;

namespace IvoryState.Gameplay.Networking
{
    /// <summary>
    /// Condition d'observation FishNet : un joueur n'est visible par un client que s'il fait partie
    /// de ses voisins les plus proches (voir <see cref="PlayerRelevance"/>).
    /// <para>
    /// S'ajoute à la GridCondition (toutes les conditions doivent être remplies). Évaluée
    /// périodiquement (Timed) par FishNet. Le calcul lourd est fait ailleurs, ici c'est une simple recherche.
    /// </para>
    /// <para>
    /// Vit dans Gameplay (et non Server) : l'asset est référencé par la scène, que les builds
    /// clients chargent aussi. Côté client, la condition n'est jamais évaluée.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Ivory State/Observers/Nearest Players Condition", fileName = "NearestPlayersCondition")]
    public sealed class NearestPlayersCondition : ObserverCondition
    {
        public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
        {
            notProcessed = false;
            return PlayerRelevance.IsRelevant(connection, NetworkObject);
        }

        public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;
    }
}
