using System.Collections.Generic;
using IvoryState.Gameplay.Economy;
using UnityEngine;

namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Gare de gbaka : le minibus de 12 places qui relie les communes. Le joueur choisit sa destination
    /// auprès de l'apprenti, paie, et arrive aussitôt à la gare choisie (trajet instantané).
    /// <para>
    /// Les destinations sont toutes les autres gares de la ville, triées par identifiant (même ordre
    /// côté client et serveur). Le prix dépend de la distance à vol d'oiseau.
    /// </para>
    /// </summary>
    public sealed class GbakaStationPoint : MenuInteractable
    {
        public const long BaseFare = 150;
        public const long FarePerKm = 300;
        public const long FareRounding = 50;
        public const long MaxFare = 600;

        private static readonly List<GbakaStationPoint> s_stations = new List<GbakaStationPoint>();

        [SerializeField] private string _stationName = "Gare d'Adjamé";
        [SerializeField] private string _commune = "Adjamé";

        [Tooltip("Où descendent les voyageurs qui arrivent ici (repère local).")]
        [SerializeField] private Vector3 _arrivalOffset = new Vector3(0f, 0f, -3f);

        private readonly List<GbakaStationPoint> _destinations = new List<GbakaStationPoint>();
        private int _destinationsVersion = -1;
        private static int s_version;
        private string _prompt;

        public override InteractionKind Kind => InteractionKind.GbakaStation;
        public string StationName => _stationName;
        public string Commune => _commune;
        public Vector3 ArrivalPosition => transform.TransformPoint(_arrivalOffset);
        public override string MenuTitle => _stationName;
        public override string MenuGreeting => "L'apprenti : « On part ! Tu vas où ? 12 places, on démarre direct ! »";
        public override string Prompt => _prompt ??= "Prendre un gbaka (" + _stationName + ")";

        public override int OptionCount => Destinations.Count;
        public override string OptionLabel(int index) => Destinations[index]._commune + " — " + Destinations[index]._stationName;
        public override long OptionPrice(int index) => FareTo(Destinations[index]);
        public override string OptionDetail(int index)
        {
            float km = Vector3.Distance(transform.position, Destinations[index].transform.position) / 1000f;
            return km.ToString("0.0") + " km, trajet direct";
        }

        /// <summary>Autres gares, triées par identifiant (même liste sur le client et le serveur).</summary>
        public IReadOnlyList<GbakaStationPoint> Destinations
        {
            get
            {
                if (_destinationsVersion != s_version)
                {
                    _destinationsVersion = s_version;
                    _destinations.Clear();
                    foreach (GbakaStationPoint station in s_stations)
                    {
                        if (station != this)
                            _destinations.Add(station);
                    }
                    _destinations.Sort((a, b) => a.Id.CompareTo(b.Id));
                }
                return _destinations;
            }
        }

        public long FareTo(GbakaStationPoint destination)
        {
            float km = Vector3.Distance(transform.position, destination.transform.position) / 1000f;
            long fare = BaseFare + (long)(km * FarePerKm);
            fare = (fare + FareRounding - 1) / FareRounding * FareRounding;
            return fare > MaxFare ? MaxFare : fare;
        }

        public void EditorSetupStation(string stationName, string commune, Vector3 arrivalOffset)
        {
            _stationName = stationName;
            _commune = commune;
            _arrivalOffset = arrivalOffset;
            _prompt = null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!s_stations.Contains(this))
            {
                s_stations.Add(this);
                s_version++;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (s_stations.Remove(this))
                s_version++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStations()
        {
            s_stations.Clear();
            s_version++;
        }

        /// <summary>Texte pour le journal : « Adjamé → Yopougon, 350 FCFA ».</summary>
        public string Describe(GbakaStationPoint destination)
        {
            return _commune + " → " + destination._commune + ", " + Money.Format(FareTo(destination));
        }
    }
}
