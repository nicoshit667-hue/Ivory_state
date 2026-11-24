using IvoryState.Core;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Server.Combat
{
    /// <summary>
    /// Tirs d'armes à feu (serveur). Le client envoie la cible qu'il vise et la direction ; on revérifie :
    /// zone protégée, portée, angle entre la direction et la cible, ligne de vue (murs), cible à pied et
    /// pas à l'hôpital. Balle : dégâts, K.O. à 0 (hôpital, crime au casier). Taser : la cible est au sol.
    /// </summary>
    internal sealed partial class CombatService
    {
        private const float ShotOriginHeight = 1.4f;
        private const float BodyCenterHeight = 1.1f;
        private const float ShotRangeTolerance = 1.5f;
        /// <summary>Écart toléré entre la direction du tir et la cible (latence, aide à la visée).</summary>
        private const float ShotAngleTolerance = 25f;

        private bool OnFire(PlayerCombat shooter, PlayerCombat target, Vector3 direction)
        {
            FirearmDef def = shooter != null ? shooter.EquippedFirearm : null;
            if (def == null || IsInVehicle(shooter))
                return false;
            long now = RentRules.Now;
            if (shooter.RecoveringUntil > now)
                return false;
            if (ProtectedZones.Contains(shooter.transform.position))
            {
                Warn(shooter, "Zone protégée : on ne tire pas ici.");
                return false;
            }

            Vector3 origin = shooter.transform.position + Vector3.up * ShotOriginHeight;
            string shooterName = shooter.TryGetComponent(out PlayerNetworkIdentity identity) ? identity.DisplayName : "Quelqu'un";

            if (target != null && IsValidHit(shooter, target, def, origin, direction, now, out Vector3 hitPoint))
            {
                ApplyHit(shooter, target, def, shooterName, hitPoint, now);
                return true;
            }

            // Raté : la traçante s'arrête au premier mur, ou à la portée maximale.
            Vector3 end = Physics.Raycast(origin, direction, out RaycastHit wall, def.Range, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                ? wall.point
                : origin + direction * def.Range;
            shooter.ServerPlayShot(end, null, shooterName, false);
            return true;
        }

        private static bool IsValidHit(PlayerCombat shooter, PlayerCombat target, FirearmDef def, Vector3 origin,
            Vector3 direction, long now, out Vector3 hitPoint)
        {
            hitPoint = target.transform.position + Vector3.up * BodyCenterHeight;
            if (!target.Owner.IsValid || IsInVehicle(target) || target.RecoveringUntil > now)
                return false;
            if (ProtectedZones.Contains(target.transform.position))
                return false;
            Vector3 toTarget = hitPoint - origin;
            float range = def.Range + ShotRangeTolerance;
            if (toTarget.sqrMagnitude > range * range)
                return false;
            if (Vector3.Angle(direction, toTarget) > ShotAngleTolerance)
                return false;
            // Un mur entre les deux : la balle s'y arrête.
            return !Physics.Linecast(origin, hitPoint, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
        }

        private void ApplyHit(PlayerCombat shooter, PlayerCombat target, FirearmDef def, string shooterName, Vector3 hitPoint, long now)
        {
            string victimName = target.TryGetComponent(out PlayerNetworkIdentity victimIdentity) ? victimIdentity.DisplayName : "ta cible";

            if (def.Stun)
            {
                shooter.ServerPlayShot(hitPoint, target, shooterName, false);
                if (target.IsStunned)
                    return;
                target.ServerStun(Firearms.StunSeconds, shooterName);
                if (target.TryGetComponent(out PlayerEconomy victimWallet))
                    victimWallet.ServerNotify(shooterName + " t'a neutralisé au taser : tu restes au sol " + (int)Firearms.StunSeconds + " s.");
                if (shooter.TryGetComponent(out PlayerEconomy shooterWallet))
                    shooterWallet.ServerNotify(victimName + " est au sol pour " + (int)Firearms.StunSeconds + " s.");
                return;
            }

            if (!target.TryGetComponent(out PlayerNeeds targetNeeds))
                return;
            targetNeeds.ServerChangeHealth(-def.Damage);
            bool knockout = targetNeeds.Health == 0;
            shooter.ServerPlayShot(hitPoint, target, shooterName, knockout);
            if (!knockout)
                return;

            target.ServerHolster();
            HospitalService.Instance?.Admit(targetNeeds, HospitalCatalog.KnockoutFee, "Blessé par balle (" + shooterName + ")");
            AddCrime(shooter, now);
            if (shooter.TryGetComponent(out PlayerEconomy economy))
                economy.ServerNotify("Tu as abattu " + victimName + " : c'est un crime, inscrit à ton casier pour 24 h. La police peut t'arrêter.");
        }
    }
}
