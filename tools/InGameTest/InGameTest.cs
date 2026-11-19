using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace KwuiInGameTest
{
    /// <summary>
    /// Plays a scripted session with nobody at the keyboard and writes what Krool World UI recorded
    /// to the BepInEx log, each line starting [KWUITEST]:
    ///
    ///   1. From the start menu: a throwaway local character and world named "kwuitest".
    ///   2. Spawns Eikthyr and kills it with hits from the local player, through Character.Damage
    ///      like a real swing. Then waits and reports the Bosses list.
    ///   3. Kills the player. Reports the Deaths list and the saved history file.
    ///   4. Logs out to the menu, enters the same world again, and reports both lists: they
    ///      must have come back from the file.
    ///   5. Quits the game.
    ///
    /// Reads the mod by reflection only, so it builds against any version of it.
    /// </summary>
    [BepInPlugin("krool.kwui.ingametest", "KWUI In-Game Test", "1.0.0")]
    public class InGameTest : BaseUnityPlugin
    {
        private const string Name = "kwuitest";
        private const float GiveUpAfter = 540f; // seconds since launch

        private static InGameTest _inst;
        private static int _menus;
        private static int _worlds;
        private bool _quitting;

        private void Awake()
        {
            _inst = this;
            new Harmony("krool.kwui.ingametest").PatchAll(typeof(InGameTest).Assembly);
            Say("loaded");
        }

        private void Update()
        {
            if (!_quitting && Time.realtimeSinceStartup > GiveUpAfter)
            {
                Say("TIMEOUT: giving up");
                Quit();
            }
        }

        private static void Say(string text) => _inst.Logger.LogMessage("[KWUITEST] " + text);

        private void Quit()
        {
            _quitting = true;
            Say("quitting");
            Application.Quit();
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class AtMenu
        {
            private static void Postfix(FejdStartup __instance) => _inst.StartCoroutine(_inst.Menu(__instance));
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class AtWorld
        {
            private static void Postfix() => _inst.StartCoroutine(_inst.World());
        }

        // ------------------------------------------------------------------ menu

        private IEnumerator Menu(FejdStartup menu)
        {
            _menus++;
            Say($"menu, time {_menus}");
            yield return new WaitForSecondsRealtime(5f);
            if (_menus > 2)
            {
                Say("FAIL: back at the menu a third time");
                Quit();
                yield break;
            }
            try
            {
                if (_menus == 1 && !PlayerProfile.HaveProfile(Name))
                {
                    var profile = new PlayerProfile(Name, FileHelpers.FileSource.Local);
                    profile.SetName("Kwuitest");
                    profile.m_firstSpawn = false; // no valkyrie ride
                    profile.Save();
                    Say("character created");
                }
                Game.SetProfile(Name, FileHelpers.FileSource.Local);
                var world = global::World.GetCreateWorld(Name, FileHelpers.FileSource.Local);
                ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
                ZNet.SetServer(true, false, false, world.m_name, "", world);
                ZNet.ResetServerHost();
                AccessTools.Field(typeof(FejdStartup), "m_startingWorld").SetValue(menu, true);
                AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(menu, null);
                Say("loading the world");
            }
            catch (Exception e)
            {
                Say($"FAIL at the menu: {e}");
                Quit();
            }
        }

        // ------------------------------------------------------------------ world

        private IEnumerator World()
        {
            _worlds++;
            int visit = _worlds;
            float start = Time.realtimeSinceStartup;
            while (Player.m_localPlayer == null || Hud.instance == null)
            {
                if (Time.realtimeSinceStartup - start > 240f)
                {
                    Say("FAIL: no player after 240 s");
                    Quit();
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(8f);
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Say("FAIL: the player went away");
                Quit();
                yield break;
            }
            Say($"in the world as {player.GetPlayerName()}, visit {visit}, profile file {Game.instance.GetPlayerProfile().GetFilename()}, id {Game.instance.GetPlayerProfile().GetPlayerID()}");
            Report("on entry");

            if (visit >= 2)
            {
                Expect("after logging back in, the boss kill is still listed", Lists().bosses == 1);
                Expect("after logging back in, the death is still listed", Lists().deaths == 1);
                Say(_failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_failures})");
                Say("DONE");
                yield return new WaitForSecondsRealtime(2f);
                Quit();
                yield break;
            }

            // ---- the boss
            player.SetGodMode(true); // a new character has 25 health; the boss must die first
            Character boss = null;
            try
            {
                var prefab = ZNetScene.instance.GetPrefab("Eikthyr");
                var pos = player.transform.position + player.transform.forward * 8f + Vector3.up;
                boss = Instantiate(prefab, pos, Quaternion.identity).GetComponent<Character>();
                Say($"spawned {boss.m_name}: m_boss={boss.m_boss} owner={boss.IsOwner()} health={boss.GetHealth():0} deathAnimation={boss.m_deathAnimation} id={boss.GetZDOID()}");
            }
            catch (Exception e)
            {
                Say($"FAIL spawning the boss: {e}");
            }
            if (boss != null)
            {
                yield return new WaitForSecondsRealtime(2f);
                int hits = 0;
                while (boss != null && !boss.IsDead() && boss.GetHealth() > 0f && hits < 300)
                {
                    var hit = new HitData();
                    hit.m_damage.m_blunt = 40f;
                    hit.m_damage.m_fire = 8f;
                    hit.m_point = boss.GetCenterPoint();
                    hit.m_dir = (boss.transform.position - player.transform.position).normalized;
                    hit.m_skill = Skills.SkillType.Clubs;
                    hit.m_hitType = HitData.HitType.PlayerHit;
                    hit.SetAttacker(player);
                    boss.Damage(hit);
                    hits++;
                    if (hits == 3)
                    {
                        Report("after three hits");
                        Expect("a fight is open after three hits", Lists().fightsOpen == 1);
                    }
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                Say($"stopped hitting after {hits} hits; boss object {(boss == null ? "gone" : $"still here, dead={boss.IsDead()} health={boss.GetHealth():0}")}");
                for (int i = 0; i < 5; i++)
                {
                    yield return new WaitForSecondsRealtime(4f);
                    Report($"{(i + 1) * 4} s after the last hit{(boss == null ? "" : " (boss object still here)")}");
                }
                Expect("the boss kill is listed", Lists().bosses == 1);
                Expect("no fight is left open", Lists().fightsOpen == 0);
            }

            // ---- the player's death
            player.SetGodMode(false);
            try
            {
                var kill = new HitData();
                kill.m_damage.m_damage = 100000f;
                kill.m_point = player.GetCenterPoint();
                kill.m_hitType = HitData.HitType.Fall;
                player.Damage(kill);
                Say("killed the player");
            }
            catch (Exception e)
            {
                Say($"FAIL killing the player: {e}");
            }
            yield return new WaitForSecondsRealtime(4f);
            Report("after the death");
            Expect("the death is listed", Lists().deaths == 1);
            Files();
            yield return new WaitForSecondsRealtime(14f);
            Report("after the respawn");

            // ---- out and in again: the lists must come back from the file
            Say("logging out");
            try
            {
                Game.instance.Logout();
            }
            catch (Exception e)
            {
                Say($"FAIL logging out: {e}");
                Quit();
            }
        }

        // ------------------------------------------------------------------ reading the mod

        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static int Count(Type type, string field)
        {
            var value = type?.GetField(field, Any)?.GetValue(null) as ICollection;
            return value != null ? value.Count : -1;
        }

        private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

        private static int _failures;

        private static void Expect(string what, bool ok)
        {
            if (!ok)
                _failures++;
            Say($"{(ok ? "ok  " : "FAIL")}: {what}");
        }

        private static (int bosses, int deaths, int fightsOpen) Lists()
        {
            var mod = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "KroolWorldUI");
            var fights = mod?.GetType("KroolWorldUI.Features.BossFights");
            var deaths = mod?.GetType("KroolWorldUI.Features.DeathRecap");
            return (Count(fights, "History"), Count(deaths, "History"), Count(fights, "_fights"));
        }

        private static void Report(string when)
        {
            try
            {
                var mod = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "KroolWorldUI");
                if (mod == null)
                {
                    Say($"{when}: FAIL, Krool World UI is not loaded");
                    return;
                }
                var fights = mod.GetType("KroolWorldUI.Features.BossFights");
                var deaths = mod.GetType("KroolWorldUI.Features.DeathRecap");
                Say($"{when}: bosses={Count(fights, "History")} deaths={Count(deaths, "History")} fightsOpen={Count(fights, "_fights")} pending={Count(fights, "_pending")}");
                if (fights?.GetField("History", Any)?.GetValue(null) is IList list && list.Count > 0)
                {
                    var recap = list[0];
                    var fighters = Get(recap, "Fighters") as IList;
                    Say($"   newest boss recap: {Get(recap, "Prefab")}, total {Get(recap, "Total")}, {Get(recap, "Seconds")} s, {fighters?.Count} fighters");
                    if (fighters != null)
                        foreach (var f in fighters)
                            Say($"      {Get(Get(f, "Who"), "Name")}: total {Get(f, "Total")}, hits {Get(f, "Hits")}, local {Get(f, "IsLocal")}, estimated {Get(f, "Estimated")}, {(Get(f, "Sources") as IList)?.Count} sources");
                }
                if (deaths?.GetField("History", Any)?.GetValue(null) is IList dlist && dlist.Count > 0)
                {
                    var d = dlist[0];
                    Say($"   newest death recap: killer cause {Get(Get(d, "Killer"), "Cause")}, taken {Get(d, "Taken")}, {(Get(d, "Rows") as IList)?.Count} rows");
                }
            }
            catch (Exception e)
            {
                Say($"{when}: FAIL reading the mod: {e}");
            }
        }

        private static void Files()
        {
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "KroolWorldUI");
                if (!Directory.Exists(dir))
                {
                    Say($"history folder: missing ({dir})");
                    return;
                }
                foreach (var f in Directory.GetFiles(dir))
                    Say($"history file: {Path.GetFileName(f)}, {new FileInfo(f).Length} bytes");
            }
            catch (Exception e)
            {
                Say($"FAIL listing history files: {e}");
            }
        }
    }
}
