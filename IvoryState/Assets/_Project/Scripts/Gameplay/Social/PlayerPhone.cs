using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace IvoryState.Gameplay.Social
{
    /// <summary>État d'un appel, vu par un joueur.</summary>
    public enum CallState : byte
    {
        Idle,       // pas d'appel
        Calling,    // on appelle quelqu'un, ça sonne chez lui
        Ringing,    // quelqu'un nous appelle
        Connected,  // en communication
    }

    /// <summary>Snapshot réseau du profil d'un joueur (soi ou un autre joueur connecté).</summary>
    public sealed class PlayerProfileSnapshot
    {
        public bool Found;
        public int TargetObjectId;
        public string DisplayName;
        public int Xp;
        public int Level;
        public string RankTitle;
        public bool IsSelf;
        public bool IsHomeless;
        public byte CurrentOutfit;
        public int OwnedOutfitsMask;
        public bool HasLicense;
        public string CarsCsv;
        public byte RentalPaint;
        public bool HasRental;
        public int StudioId;
        public string Roommate;
        public int Prop1Id;
        public bool Prop1Owner;
        public int Prop2Id;
        public bool Prop2Owner;
        public long NetWorth;
        public long Balance;
        public long Cash;
        public int BagCount;
        public string BagSummary;
        public int Crimes;
        public byte Health;
        public byte Satiety;
        public byte Hydration;
        public Player.PlayerJobStatus JobStatus;
        public int ProfileViews;
        public int PrivacyMask;
        public string ResidenceDistrict;
        public string CommercesSummary;
        public int CommercesCount;
    }

    /// <summary>
    /// Règles d'appel côté serveur (implémentées par l'assembly Server, comme pour le chat) :
    /// annuaire, mise en relation, occupation, délai de sonnerie, relais de la voix.
    /// </summary>
    public interface IPhoneSwitchboard
    {
        void Directory(PlayerPhone phone);
        void Call(PlayerPhone caller, int targetObjectId);
        void Answer(PlayerPhone phone, bool accept);
        void HangUp(PlayerPhone phone);
        void Voice(PlayerPhone phone, ArraySegment<byte> frame);
        void Disconnected(PlayerPhone phone);
        void ProfileDirectory(PlayerPhone phone);
        void Profile(PlayerPhone requester, int targetObjectId);
    }

    /// <summary>
    /// Téléphone du joueur : appeler un autre joueur connecté, n'importe où dans la ville.
    /// <list type="bullet">
    /// <item><b>Client</b> : demandes au serveur (annuaire, appeler, décrocher, raccrocher) et voix
    /// compressée (trames de 40 ms, canal non fiable : une trame perdue vaut mieux qu'une trame en retard).</item>
    /// <item><b>Serveur</b> : <see cref="Switchboard"/> applique les règles et relaie la voix au seul
    /// correspondant. Aucune voix n'est diffusée à d'autres joueurs.</item>
    /// </list>
    /// La capture et la lecture du son sont côté client (assembly Client) : ce composant ne transporte
    /// que des octets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerPhone : NetworkBehaviour
    {
        /// <summary>Taille maximale d'une trame de voix (octets) : au-delà, la trame est ignorée.</summary>
        public const int MaxVoiceFrame = 512;

        public static IPhoneSwitchboard Switchboard { get; set; }
        public static PlayerPhone Local { get; private set; }

        /// <summary>Annuaire reçu : identifiants d'objet et pseudos des joueurs joignables.</summary>
        public static event Action<int[], string[]> LocalDirectoryReceived;

        /// <summary>Changement d'état d'appel : (état, id du correspondant, pseudo, motif éventuel).</summary>
        public static event Action<CallState, int, string, string> LocalCallStateChanged;

        /// <summary>Trame de voix reçue du correspondant (valide seulement pendant l'appel de l'événement).</summary>
        public static event Action<ArraySegment<byte>> LocalVoiceReceived;

        /// <summary>Liste des joueurs en ligne avec niveau et statut (pour l'application Profil).</summary>
        public static event Action<int[], string[], int[], bool[]> LocalProfileDirectoryReceived;

        /// <summary>Profil détaillé d'un joueur reçu du serveur.</summary>
        public static event Action<PlayerProfileSnapshot> LocalProfileReceived;

        public CallState State { get; private set; }
        public int PeerObjectId { get; private set; } = -1;
        public string PeerName { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Switchboard = null;
            Local = null;
            LocalDirectoryReceived = null;
            LocalCallStateChanged = null;
            LocalVoiceReceived = null;
            LocalProfileDirectoryReceived = null;
            LocalProfileReceived = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
                Local = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
                Local = null;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Switchboard?.Disconnected(this);
        }

        #region Client → serveur

        public void RequestDirectory() { if (IsOwner) ServerDirectory(); }
        public void RequestProfileDirectory() { if (IsOwner) ServerProfileDirectory(); }
        public void RequestProfile(int targetObjectId) { if (IsOwner) ServerProfile(targetObjectId); }
        public void Call(int targetObjectId) { if (IsOwner) ServerCall(targetObjectId); }
        public void Answer(bool accept) { if (IsOwner) ServerAnswer(accept); }
        public void HangUp() { if (IsOwner) ServerHangUp(); }

        /// <summary>Envoie une trame de voix compressée (seulement en communication).</summary>
        public void SendVoice(ArraySegment<byte> frame)
        {
            if (IsOwner && State == CallState.Connected && frame.Count > 0 && frame.Count <= MaxVoiceFrame)
                ServerVoice(frame);
        }

        [ServerRpc] private void ServerDirectory() => Switchboard?.Directory(this);
        [ServerRpc] private void ServerProfileDirectory() => Switchboard?.ProfileDirectory(this);
        [ServerRpc] private void ServerProfile(int targetObjectId) => Switchboard?.Profile(this, targetObjectId);
        [ServerRpc] private void ServerCall(int targetObjectId) => Switchboard?.Call(this, targetObjectId);
        [ServerRpc] private void ServerAnswer(bool accept) => Switchboard?.Answer(this, accept);
        [ServerRpc] private void ServerHangUp() => Switchboard?.HangUp(this);

        [ServerRpc]
        private void ServerVoice(ArraySegment<byte> frame, Channel channel = Channel.Unreliable)
        {
            if (frame.Count <= MaxVoiceFrame)
                Switchboard?.Voice(this, frame);
        }

        #endregion

        #region Serveur → client

        /// <summary>Met à jour l'état d'appel (serveur) et le transmet au propriétaire.</summary>
        [Server]
        public void ServerSetState(CallState state, int peerObjectId, string peerName, string reason)
        {
            State = state;
            PeerObjectId = peerObjectId;
            PeerName = peerName ?? string.Empty;
            if (Owner.IsValid)
                TargetState(Owner, state, peerObjectId, PeerName, reason ?? string.Empty);
        }

        [Server]
        public void ServerSendDirectory(int[] ids, string[] names)
        {
            if (Owner.IsValid)
                TargetDirectory(Owner, ids, names);
        }

        [Server]
        public void ServerDeliverVoice(ArraySegment<byte> frame)
        {
            if (Owner.IsValid)
                TargetVoice(Owner, frame);
        }

        [Server]
        public void ServerSendProfileDirectory(int[] ids, string[] names, int[] levels, bool[] homeless)
        {
            if (Owner.IsValid)
                TargetProfileDirectory(Owner, ids, names, levels, homeless);
        }

        [Server]
        public void ServerSendProfile(
            bool found, int targetObjectId, string displayName, int xp, bool isSelf, bool isHomeless,
            byte currentOutfit, int ownedOutfitsMask, bool hasLicense, string carsCsv, byte rentalPaint, bool hasRental,
            int studioId, string roommate, int prop1Id, bool prop1Owner, int prop2Id, bool prop2Owner,
            long netWorth, long balance, long cash, int bagCount, string bagSummary,
            int crimes, byte health, byte satiety, byte hydration,
            byte jobStatus, int profileViews, int privacyMask, string residenceDistrict, string commercesSummary, int commercesCount)
        {
            if (Owner.IsValid)
            {
                TargetProfile(Owner, found, targetObjectId, displayName ?? string.Empty, xp, isSelf, isHomeless,
                    currentOutfit, ownedOutfitsMask, hasLicense, carsCsv ?? string.Empty, rentalPaint, hasRental,
                    studioId, roommate ?? string.Empty, prop1Id, prop1Owner, prop2Id, prop2Owner,
                    netWorth, balance, cash, bagCount, bagSummary ?? string.Empty,
                    crimes, health, satiety, hydration,
                    jobStatus, profileViews, privacyMask, residenceDistrict ?? string.Empty, commercesSummary ?? string.Empty, commercesCount);
            }
        }

        [TargetRpc]
        private void TargetProfileDirectory(NetworkConnection connection, int[] ids, string[] names, int[] levels, bool[] homeless)
        {
            LocalProfileDirectoryReceived?.Invoke(ids, names, levels, homeless);
        }

        [TargetRpc]
        private void TargetProfile(
            NetworkConnection connection,
            bool found, int targetObjectId, string displayName, int xp, bool isSelf, bool isHomeless,
            byte currentOutfit, int ownedOutfitsMask, bool hasLicense, string carsCsv, byte rentalPaint, bool hasRental,
            int studioId, string roommate, int prop1Id, bool prop1Owner, int prop2Id, bool prop2Owner,
            long netWorth, long balance, long cash, int bagCount, string bagSummary,
            int crimes, byte health, byte satiety, byte hydration,
            byte jobStatus, int profileViews, int privacyMask, string residenceDistrict, string commercesSummary, int commercesCount)
        {
            int level = Player.PlayerProgression.GetLevel(xp);
            Player.PlayerJobStatus safeStatus = Player.PlayerProfileMetaCatalog.IsValid(jobStatus)
                ? (Player.PlayerJobStatus)jobStatus
                : Player.PlayerJobStatus.Civil;
            LocalProfileReceived?.Invoke(new PlayerProfileSnapshot
            {
                Found = found,
                TargetObjectId = targetObjectId,
                DisplayName = displayName,
                Xp = xp,
                Level = level,
                RankTitle = Player.PlayerProgression.GetRankTitle(level),
                IsSelf = isSelf,
                IsHomeless = isHomeless,
                CurrentOutfit = currentOutfit,
                OwnedOutfitsMask = ownedOutfitsMask,
                HasLicense = hasLicense,
                CarsCsv = carsCsv,
                RentalPaint = rentalPaint,
                HasRental = hasRental,
                StudioId = studioId,
                Roommate = roommate,
                Prop1Id = prop1Id,
                Prop1Owner = prop1Owner,
                Prop2Id = prop2Id,
                Prop2Owner = prop2Owner,
                NetWorth = netWorth,
                Balance = balance,
                Cash = cash,
                BagCount = bagCount,
                BagSummary = bagSummary,
                Crimes = crimes,
                Health = health,
                Satiety = satiety,
                Hydration = hydration,
                JobStatus = safeStatus,
                ProfileViews = profileViews,
                PrivacyMask = privacyMask,
                ResidenceDistrict = residenceDistrict,
                CommercesSummary = commercesSummary,
                CommercesCount = commercesCount,
            });
        }

        [TargetRpc]
        private void TargetState(NetworkConnection connection, CallState state, int peerObjectId, string peerName, string reason)
        {
            State = state;
            PeerObjectId = peerObjectId;
            PeerName = peerName;
            LocalCallStateChanged?.Invoke(state, peerObjectId, peerName, reason);
        }

        [TargetRpc]
        private void TargetDirectory(NetworkConnection connection, int[] ids, string[] names)
        {
            LocalDirectoryReceived?.Invoke(ids, names);
        }

        [TargetRpc]
        private void TargetVoice(NetworkConnection connection, ArraySegment<byte> frame, Channel channel = Channel.Unreliable)
        {
            LocalVoiceReceived?.Invoke(frame);
        }

        #endregion
    }
}
