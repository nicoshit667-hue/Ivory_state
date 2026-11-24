using System.Collections.Generic;
using System.IO;
using System.Text;
using IvoryState.Gameplay.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace IvoryState.Editor.Debugging
{
    /// <summary>
    /// Diagnostic (batchmode) : pour chaque point d'interaction de la scène, le sol sous lui, le NavMesh, la place
    /// libre pour s'y tenir à portée et les obstacles proches. Écrit Builds/interaction-probe.txt.
    /// <c>Unity -batchmode -projectPath IvoryState -executeMethod IvoryState.Editor.Debugging.InteractionProbe.Run -quit</c>
    /// </summary>
    public static class InteractionProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Bootstrap.unity");
            Physics.SyncTransforms();
            var report = new StringBuilder();
            var points = Object.FindObjectsByType<WorldInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int unreachable = 0, noNav = 0, total = 0;
            var counts = new Dictionary<string, int>();
            foreach (WorldInteractable point in points)
            {
                if (point is WorldDoor)
                    continue;
                total++;
                Vector3 p = point.transform.position;
                string kind = point.GetType().Name;
                counts[kind] = counts.TryGetValue(kind, out int c) ? c + 1 : 1;

                string ground = "aucun";
                float groundY = float.NaN;
                if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                {
                    groundY = hit.point.y;
                    ground = hit.collider.name + " y=" + hit.point.y.ToString("F2");
                }
                bool nav = NavMesh.SamplePosition(p, out NavMeshHit navHit, 2.5f, NavMesh.AllAreas);
                if (!nav)
                    noNav++;

                // Places où un joueur (capsule r 0,35, h 1,8) tient debout à portée du point.
                int free = 0;
                string blocker = null;
                for (int a = 0; a < 12; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward;
                    foreach (float r in new[] { 0.8f, 1.4f, 2.0f })
                    {
                        Vector3 s = p + dir * r;
                        if (!Physics.Raycast(s + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        Vector3 feet = floor.point;
                        if ((feet - p).sqrMagnitude > point.Range * point.Range)
                            continue;
                        Collider[] overlaps = Physics.OverlapCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.45f, 0.33f, ~0, QueryTriggerInteraction.Ignore);
                        if (overlaps.Length == 0)
                            free++;
                        else if (blocker == null)
                            blocker = overlaps[0].name + (overlaps[0].transform.parent != null ? "<" + overlaps[0].transform.parent.name : "");
                    }
                }
                if (free == 0)
                    unreachable++;
                if (free == 0 || !nav || point is ShopPoint)
                {
                    report.AppendLine(kind + " #" + point.Id + " « " + point.DisplayName + " » p=" + p.ToString("F2") + " sol=" + ground +
                                      " navmesh=" + (nav ? navHit.position.ToString("F2") : "non") + " places libres=" + free +
                                      (blocker != null ? " obstacle=" + blocker : "") +
                                      (!float.IsNaN(groundY) ? " écart=" + (p.y - groundY).ToString("F2") : ""));
                }
            }
            var head = new StringBuilder();
            head.AppendLine("Points : " + total + ", inaccessibles : " + unreachable + ", hors NavMesh : " + noNav);
            foreach (var pair in counts)
                head.AppendLine("  " + pair.Key + " : " + pair.Value);
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "interaction-probe.txt");
            File.WriteAllText(path, head.ToString() + report);
            Debug.Log("[Probe] " + head);
        }
    }
}
