using BepInEx;
using BepInEx.Logging;
using DG.Tweening;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniFighters
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {   
        private void Awake()
        {
            Log = Logger;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name}");
        }
        internal static ManualLogSource Log;
    }

    public class Manager: MonoBehaviour
    {
        public static void AddScore()
        {
            score++;
        }

        public static bool IsSubstractScoreGameOver()
        {
            return --score< 0;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            score = 0;
        }

        public static Manager Instance;
        public static int score = 0;
    }

    public class AircraftTag: MonoBehaviour
    {
        public static bool IsInfrontOfMe(Aircraft friendly, Aircraft enemy)
        {
            Vector2 friendlyPosition = new Vector2(friendly.AP.transform.position.x, friendly.AP.transform.position.y);
            Vector2 enemyPosition = new Vector2(enemy.AP.transform.position.x, enemy.AP.transform.position.y);
            float theta = (float)Math.PI / 180 * friendly.heading;
            Vector2 heading = new Vector2((float)Math.Sin(theta), (float)Math.Cos(theta));

            float searchStep = 0;
            Vector2 currentPosition = friendlyPosition + heading * searchStep;
            float distance = Vector2.Distance(currentPosition, enemyPosition);
            float prevDistance = 1e9f;
            while (searchStep < MAX_SEARCH_DISTANCE && distance < prevDistance)
            {
                if (distance < DISTANCE_THRESHOLD)
                {
                    return true;
                }
                searchStep += SEARCH_STEP_GRADIENT;
                prevDistance = distance;
                currentPosition = friendlyPosition + heading * searchStep;
                distance = Vector2.Distance(currentPosition, enemyPosition);
            }

            return false;
        }

        public static IEnumerator EnemyDownCoroutine(AircraftTag friendly, AircraftTag enemy)
        {
            // Slow down while attacking.
            friendly.aircraft_.speed = friendly.GetTargetSpeed() / 2;

            enemy.StartCoroutine(DamageCoroutine(enemy.aircraft_));

            yield return new WaitForSeconds(3f);

            // Resume normal speed.
            friendly.aircraft_.speed = friendly.GetTargetSpeed();

            // Enemy down.
            enemy.aircraft_.ConditionalDestroy();
            Manager.AddScore();

            if (--friendly.ammo_ <= 0)
            {
                // Time to head back.
                Vector3 position = friendly.aircraft_.AP.transform.position;
                float heading = friendly.aircraft_.heading;

                friendly.aircraft_.ConditionalDestroy();

                Aircraft newAircraft = AircraftManager.Instance.CreateInboundAircraft(position, heading);

                AircraftTag newFriendly = newAircraft.gameObject.GetComponent<AircraftTag>();
                if (newFriendly == null)
                {
                    newFriendly = newAircraft.gameObject.AddComponent<AircraftTag>();
                }
                newFriendly.aircraft_ = newAircraft;
                newFriendly.friendly_ = true;
            }

            friendly.activeCoroutine_ = null;
        }

        private static IEnumerator DamageCoroutine(Aircraft aircraft)
        {
            while (aircraft != null)
            {
                aircraft.AP.GetComponent<Renderer>().material.color = Color.white;
                yield return new WaitForSeconds(0.1f);
                aircraft.AP.GetComponent<Renderer>().material.color = Color.red;
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void Update()
        {
            if (aircraft_ == null)
            {
                return;
            }

            if (friendly_)
            {
                aircraft_.targetSpeed = GetTargetSpeed();
                aircraft_.AP.GetComponent<Renderer>().material.color = Color.green;
                if (aircraft_.direction == Aircraft.Direction.Inbound)
                {
                    aircraft_.Panel.GetComponent<Renderer>().material.color = Color.green;
                }
            }
            else
            {
                aircraft_.targetSpeed = 16f;
                aircraft_.AP.GetComponent<Renderer>().material.color = Color.red;
                aircraft_.Panel.GetComponent<Renderer>().material.color = Color.red;
            }
        }

        private float GetTargetSpeed()
        {
            switch(aircraft_.colorCode)
            {
                // TODO: color based plane speed/turn radius.
                case ColorCode.Option.Yellow:
                case ColorCode.Option.Red:
                case ColorCode.Option.Orange:
                case ColorCode.Option.LightBlue:
                case ColorCode.Option.Green:
                case ColorCode.Option.Pink:
                    break;
            }
            return 36f;
        }

        public bool friendly_ = true;
        public Aircraft aircraft_;
        public int ammo_ = 2;
        public IEnumerator activeCoroutine_ = null;
        private const float SEARCH_STEP_GRADIENT = 0.1f;
        private const float MAX_SEARCH_DISTANCE = 4f;
        private const float DISTANCE_THRESHOLD = 0.5f;
    }

    // Add friendly tag to Aircraft, and slow down incoming aircraft.
    [HarmonyPatch(typeof(Aircraft), "Start", new Type[] {})]
    class PatchAircraftStart
    {
        static void Postfix(ref Aircraft __instance)
        {
            AircraftTag tag = __instance.gameObject.GetComponent<AircraftTag>();
            if (tag == null)
            {
                tag = __instance.gameObject.AddComponent<AircraftTag>();
                tag.aircraft_ = __instance;
                if (__instance.direction == Aircraft.Direction.Inbound)
                {
                    // Default arrival are not friendly.
                    tag.friendly_ = false;
                }
            }
            __instance.SetFieldValue<float>("acceleration", 0.04f);
        }
    }

    // Patch for fighting.
    [HarmonyPatch(typeof(Aircraft), "OnTriggerStay2D", new Type[] {typeof(Collider2D)})]
    class PatchOnTriggerStay2D
    {
        static bool Prefix(Collider2D other, ref bool ___mainMenuMode, ref Aircraft __instance)
        {

            if (___mainMenuMode || !((Component)(object)other).CompareTag("CollideCheck"))
            {
                return false;
            }

            if (((Component)(object)other).gameObject.layer == LayerMask.NameToLayer("Waypoint"))
            {
                return true;
            }

            if (other.GetComponent<AircraftRef>() != null)
            {
                // Do not sound TCAS when altitudes are different.
                Aircraft aircraft = other.GetComponent<AircraftRef>().aircraft;
                if (other.name == "TCAS")
                {
                    AircraftTag tag1 = __instance.GetComponent<AircraftTag>();
                    AircraftTag tag2 = aircraft.GetComponent<AircraftTag>();
                    if (tag1 == null || tag2 == null)
                    {
                        return true;
                    }

                    // Don't shot down friendly.
                    if (tag1.friendly_ == tag2.friendly_)
                    {
                        return true;
                    }

                    if (tag1.friendly_ && tag1.aircraft_.direction == Aircraft.Direction.Outbound && 
                        tag1.activeCoroutine_ == null && AircraftTag.IsInfrontOfMe(tag1.aircraft_, tag2.aircraft_))
                    {
                        // Shot down enemy.
                        tag1.activeCoroutine_ = AircraftTag.EnemyDownCoroutine(tag1, tag2);
                        Manager.Instance.StartCoroutine(tag1.activeCoroutine_);
                        return false;
                    }
                }
            }
            return true;
        }
    }

    // Patch for fighting.
    [HarmonyPatch(typeof(Aircraft), "OnTriggerEnter2D", new Type[] {typeof(Collider2D)})]
    class PatchOnTriggerEnter2D
    {
        static bool Prefix(Collider2D other, ref bool ___mainMenuMode, ref Aircraft __instance)
        {

            if (___mainMenuMode || !((Component)(object)other).CompareTag("CollideCheck"))
            {
                return false;
            }

            if (((Component)(object)other).gameObject.layer == LayerMask.NameToLayer("Waypoint"))
            {
                return true;
            }

            if (other.GetComponent<AircraftRef>() != null)
            {
                // Do not sound TCAS when altitudes are different.
                Aircraft aircraft = other.GetComponent<AircraftRef>().aircraft;
                if (other.name == "TCAS")
                {
                    AircraftTag tag1 = __instance.GetComponent<AircraftTag>();
                    AircraftTag tag2 = aircraft.GetComponent<AircraftTag>();
                    if (tag1 == null || tag2 == null)
                    {
                        return true;
                    }

                    // Don't shot down friendly.
                    if (tag1.friendly_ == tag2.friendly_)
                    {
                        return true;
                    }

                    if (tag1.friendly_ && tag1.aircraft_.direction == Aircraft.Direction.Outbound && 
                        tag1.activeCoroutine_ == null && AircraftTag.IsInfrontOfMe(tag1.aircraft_, tag2.aircraft_))
                    {
                        // Shot down enemy.
                        tag1.activeCoroutine_ = AircraftTag.EnemyDownCoroutine(tag1, tag2);
                        Manager.Instance.StartCoroutine(tag1.activeCoroutine_);
                        return false;
                    }
                }
            }
            return true;
        }
    }

    // Only allow friendly a
    [HarmonyPatch(typeof(Aircraft), "AircraftCollideGameOver", new Type[] {typeof(Aircraft), typeof(Aircraft)})]
    class PatchAircraftCollideGameOver
    {
        static bool Prefix(Aircraft aircraft1, Aircraft aircraft2)
        {
            AircraftTag tag1 = aircraft1.gameObject.GetComponent<AircraftTag>();
            AircraftTag tag2 = aircraft2.gameObject.GetComponent<AircraftTag>();
            if (tag1 == null || tag2 == null)
            {
                return false;
            }

            // Friendly collide with enemy without shooting it down.
            if (tag1.friendly_ != tag2.friendly_ && tag1.friendly_ || tag2.friendly_)
            {
                if (!Manager.IsSubstractScoreGameOver())
                {
                    aircraft1.ConditionalDestroy();
                    aircraft2.ConditionalDestroy();
                    return false;
                }
                return true;
            }

            // Friendly collides.
            if (tag1.friendly_ && tag2.friendly_)
            {
                // Two friendly down.
                Manager.score--;
                if (!Manager.IsSubstractScoreGameOver())
                {
                    aircraft1.ConditionalDestroy();
                    aircraft2.ConditionalDestroy();
                    return false;
                }
                return true;
            }

            return false;
        }
    }

    // Disable control for incoming aircraft.
    [HarmonyPatch(typeof(Aircraft), "OnPointDown", new Type[] {})]
    class PatchOnPointDown
    {
        static bool Prefix(ref Aircraft __instance)
        {
            if (__instance.direction == Aircraft.Direction.Inbound)
            {
                AircraftTag tag = __instance.gameObject.GetComponent<AircraftTag>();
                if (tag == null)
                {
                    return false;
                }
                return tag.friendly_;
            }
            return true;
        }
    }

    // Fully upgrade before starting.
    [HarmonyPatch(typeof(UpgradeManager), "Start", new Type[] {})]
    class PatchUpgradeManagerStart
    {
        static void Postfix(ref UpgradeManager __instance)
        {
            // Max-out all airspace.
            Camera.main.DOOrthoSize(LevelManager.Instance.maximumCameraOrthographicSize, 0.5f).SetUpdate(isIndependentUpdate: true);

            // Attach Manager to ESC button.
            GameObject esc_button = GameObject.Find("ESC_Button");
            esc_button.gameObject.AddComponent<Manager>();
        }
    }

    // Remove not useful upgrades.
    [HarmonyPatch(typeof(UpgradeManager), "ProcessOptionProbs", new Type[] {})]
    class PatchProcessOptionProbs
    {
        static void Postfix(ref List<float> __result)
        {
            __result[4] = 0; // AIRSPACE
            __result[6] = 0; // AUTO_HEADING_PROP
            __result[8] = 0; // AUTO_LANDING_PROP
            __result[9] = 0; // TAKING_OFF_PROP
        }
    }

    // Do not allow camera size change.
    [HarmonyPatch(typeof(LevelManager), "Start", new Type[] {})]
    class PatchLevelManagerStart
    {
        static void Postfix()
        {
            LevelManager.CameraSizeIncByFailGenWaypoint = 0f;
        }
    }

    // Do not allow new destination waypoint to spawn.
    [HarmonyPatch(typeof(WaypointManager), "CreateNewWaypoint", new Type[] {
        typeof(Vector3), typeof(ColorCode.Option), typeof(ShapeCode.Option), typeof(bool), typeof(bool)})]
    class PatchCreateNewWaypoint
    {
        static bool Prefix(Vector3 position, ColorCode.Option colorOption, ShapeCode.Option shapeOption,
                           bool AutoDestroy, bool checkExist)
        {
            return false;
        }
    }

    // Do not allow new destination waypoint to spawn.
    [HarmonyPatch(typeof(WaypointManager), "CreateNewWaypoint", new Type[] {})]
    class PatchCreateNewWaypointDefault
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // Do not allow new destination waypoint to spawn.
    [HarmonyPatch(typeof(WaypointManager), "CreateNewWaypoint", new Type[] {typeof(Vector3), typeof(bool)})]
    class PatchCreateNewWaypointVector
    {
        static bool Prefix(Vector3 position, bool AutoDestroy)
        {
            return false;
        }
    }

    // Do not allow new destination waypoint to spawn.
    [HarmonyPatch(typeof(WaypointManager), "CreateNewWaypoint", new Type[] {typeof(ColorCode.Option), typeof(ShapeCode.Option)})]
    class PatchCreateNewWaypointCode
    {
        static bool Prefix(ColorCode.Option color, ShapeCode.Option shape)
        {
            return false;
        }
    }

    // Do not allow fixed destination waypoint to disappear.
    [HarmonyPatch(typeof(WaypointManager), "HasSameColorShapeWaypoint", new Type[] {typeof(ColorCode.Option), typeof(ShapeCode.Option)})]
    class PatchHasSameColorShapeWaypoint
    {
        static void Postfix(ColorCode.Option colorCode, ShapeCode.Option shapeCode, ref bool __result)
        {
            __result = true;
        }
    }

    // Restore to specified speed.
    [HarmonyPatch(typeof(Aircraft), "SetFlyHeading", new Type[] {})]
    class PatchSetFlyHeading
    {
        static bool Prefix(ref Aircraft __instance, ref object[] __state)
        {
            __state = new object[] {__instance.targetSpeed};
            return true;
        }
    
        static void Postfix(ref Aircraft __instance, ref object[] __state)
        {
            if (__instance.targetSpeed > 0 && __state.Length == 1) 
            { 
                __instance.targetSpeed = (float)__state[0];
            }
        }
    }

    // Restore to specified speed.
    [HarmonyPatch(typeof(Aircraft), "SetFlyHeading", new Type[] {typeof(float)})]
    class PatchSetFlyHeadingFloat
    {
        static bool Prefix(float heading,  ref Aircraft __instance, ref object[] __state)
        {
            __state = new object[] {__instance.targetSpeed};
            return true;
        }

        static void Postfix(float heading,  ref Aircraft __instance, ref object[] __state)
        {
            if (__instance.targetSpeed > 0 && __state.Length == 1) 
            { 
                __instance.targetSpeed = (float)__state[0];
            }
        }
    }

    // Restore to specified speed.
    [HarmonyPatch(typeof(Aircraft), "SetVectorTo", new Type[] {typeof(WaypointAutoHover)})]
    class PatchSetVectorToWaypointAutoHover
    {
        static bool Prefix(WaypointAutoHover waypoint,  ref Aircraft __instance, ref object[] __state)
        {
            __state = new object[] {__instance.targetSpeed};
            return true;
        }

        static void Postfix(WaypointAutoHover waypoint, ref Aircraft __instance, ref object[] __state)
        {
            if (__instance.targetSpeed > 0 && __state.Length == 1)
            {
                __instance.targetSpeed = (float)__state[0];
            }
        }
    }

    // Restore to specified speed.
    [HarmonyPatch(typeof(Aircraft), "SetVectorTo", new Type[] {typeof(PlaceableWaypoint)})]
    class PatchSetVectorToPlaceableWaypoint
    {
        static bool Prefix(PlaceableWaypoint waypoint, ref Aircraft __instance, ref object[] __state)
        {
            __state = new object[] {__instance.targetSpeed};
            return true;
        }

        static void Postfix(PlaceableWaypoint waypoint, ref Aircraft __instance, ref object[] __state)
        {
            if (__instance.targetSpeed > 0 && __state.Length == 1)
            { 
                __instance.targetSpeed = (float)__state[0];
            }
        }
    }

    // Restore to specified speed.
    [HarmonyPatch(typeof(Aircraft), "OnPointUp", new Type[] {typeof(bool)})]
    class PatchOnPointUp
    {
        static bool Prefix(bool external, ref Aircraft __instance, ref object[] __state)
        {
            __state = new object[] {__instance.targetSpeed};
            return true;
        }

        static void Postfix(bool external, ref Aircraft __instance, ref object[] __state)
        {
            if (__instance.targetSpeed > 0 && __state.Length == 1) 
            { 
                __instance.targetSpeed = (float)__state[0];
            }
        }
    }

    // Point based game over system.
    [HarmonyPatch(typeof(Aircraft), "AircraftOOBGameOver", new Type[] {typeof(Aircraft)})]
    class PatchAircraftOOBGameOver
    {
        static bool Prefix(Aircraft aircraft)
        {
            if (!Manager.IsSubstractScoreGameOver())
            {
                aircraft.ConditionalDestroy();
                return false;
            }
            return true;
        }
    }

    // Point based game over system.
    [HarmonyPatch(typeof(Aircraft), "AircraftTerrainGameOver", new Type[] { typeof(Aircraft)})]
    class PatchAircraftTerrainGameOver
    {
        static bool Prefix(Aircraft aircraft)
        {
            if (!Manager.IsSubstractScoreGameOver())
            {
                aircraft.ConditionalDestroy();
                return false;
            }
            return true;
        }
    }

    // Skip original point calculation.
    [HarmonyPatch(typeof(GameDataWhiteBoard), "OnAircraftTookOff", new Type[] {})]
    class PatchOnAircraftTookOff
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // Skip original point calculation.
    [HarmonyPatch(typeof(GameDataWhiteBoard), "OnAircraftLanded", new Type[] {})]
    class PatchOnAircraftLanded
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // Skip original point calculation.
    [HarmonyPatch(typeof(GameDataWhiteBoard), "Update", new Type[] {})]
    class PatchGameDataWhiteBoardUpdate
    {
        static bool Prefix(GameDataWhiteBoard __instance, ref int ____tookOffCount)
        {
            ____tookOffCount = Manager.score;
            return true;
        }
    }

    // Enemy do not cause issue on restricted area.
    [HarmonyPatch(typeof(RestrictedAreaManager), "AreaEnter", new Type[] { typeof(Aircraft) })]
    class PatchAreaEnter
    {
        static bool Prefix(Aircraft aircraft)
        {
            AircraftTag tag = aircraft.gameObject.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return false;
            }
            return tag.friendly_;
        }
    }

    // Do not show directional arrow.
    [HarmonyPatch(typeof(RestrictedLineIndicator), "Update", new Type[] {})]
    class PatchRestrictedLineIndicatorUpdate
    {
        static bool Prefix(ref RestrictedLineIndicator __instance)
        {
            __instance.TArrow.gameObject.SetActive(false);
            __instance.FArrow.gameObject.SetActive(false);
            return false;
        }
    }


    [HarmonyPatch(typeof(LevelManager), "RestrictedGameOver", new Type[] { typeof(Aircraft) })]
    class AircraftCrashPatcher2
    {
        static bool Prefix(Aircraft aircraft)
        {
            return Manager.IsSubstractScoreGameOver();
        }
    }

    // Adapted from https://github.com/burningtnt/MiniAirways-EndlessGame
    [HarmonyPatch(typeof(LevelManager), "CrowdedGameOver", new Type[] { typeof(TakeoffTaskManager) })]
    class AircraftCrashPatcher3
    {
        static bool Prefix(TakeoffTaskManager takeoffTaskManager, ref LevelManager __instance)
        {
            for (int i = takeoffTaskManager.hangingTakeoffTasks.Count - 1; i >= 0; i--)
            {
                TakeoffTask task = takeoffTaskManager.hangingTakeoffTasks[i];
                if (task.GetFieldValue<float>("currentKnockOutTimer") > task.knockOutTime && !GameOverManager.Instance.GameOverFlag && Time.deltaTime > 0f)
                {
                    task.StopHanging();
                    takeoffTaskManager.hangingTakeoffTasks.RemoveAt(i);

                    task.StartCoroutine(HideCoroutine(task.Panel.GetComponent<SpriteRenderer>(), task.gameObject));
                }
            }

            TakeoffTaskManager.ReArrangeAircrafts();

            return false;
        }

        private static IEnumerator HideCoroutine(SpriteRenderer component, GameObject gameObject)
        {
            Sequence sequence = DOTween.Sequence();
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Play().SetUpdate(isIndependentUpdate: true);

            yield return new WaitForSecondsRealtime(1.25f);

            UnityEngine.Object.Destroy(gameObject);
        }
    }

    // Adapted from https://github.com/burningtnt/MiniAirways-EndlessGame
    [HarmonyPatch(typeof(TakeoffTaskManager), "ReArrangeAircrafts", new Type[] { })]
    class TakeoffTaskManagerPatcher
    {
        static bool Prefix()
        {
            TakeoffTaskManager instance = TakeoffTaskManager.Instance;
            int firstEmptyApron = instance.Aprons.Count;

            for (int emptyApronIndex = 0; emptyApronIndex < instance.Aprons.Count; emptyApronIndex++)
            {
                Apron emptyApron = instance.Aprons[emptyApronIndex];
                if (emptyApron.takeoffTask)
                {
                    continue;
                }

                for (int usedApronIndex = emptyApronIndex + 1; usedApronIndex < instance.Aprons.Count; usedApronIndex++)
                {
                    Apron usedApron = instance.Aprons[usedApronIndex];
                    if (!usedApron.takeoffTask)
                    {
                        continue;
                    }

                    usedApron.takeoffTask.apron = emptyApron;
                    usedApron.takeoffTask.gameObject.transform.SetParent(emptyApron.transform);
                    usedApron.takeoffTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);

                    emptyApron.isOccupied = true;
                    emptyApron.takeoffTask = usedApron.takeoffTask;
                    usedApron.isOccupied = false;
                    usedApron.takeoffTask = null;
                    break;
                }

                if (!emptyApron.isOccupied) // This apron is still empty
                {
                    firstEmptyApron = emptyApronIndex;
                    break;
                }
            }

            int emptyApronCount = instance.Aprons.Count - firstEmptyApron;
            if (instance.hangingTakeoffTasks.Count > 0)
            {
                // Step 1: Move all hanging jobs to empty aprons.
                int scheduleableHangingTaskCount = Math.Min(emptyApronCount, instance.hangingTakeoffTasks.Count);
                for (int i = 0; i < scheduleableHangingTaskCount; i++)
                {
                    TakeoffTask hangingTask = instance.hangingTakeoffTasks[i];
                    Apron targetApron = instance.Aprons[firstEmptyApron + i];

                    targetApron.CreateTask(hangingTask);
                    hangingTask.apron = targetApron;
                    hangingTask.StopHanging();

                    hangingTask.gameObject.transform.SetParent(targetApron.transform);
                    hangingTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);
                }

                // Step 2: Forget handled jobs.
                instance.hangingTakeoffTasks.RemoveRange(0, scheduleableHangingTaskCount);

                // TODO: Cache this array instance.
                Apron[] virtualAprons = new Apron[] { instance.virtualApron, instance.virtualApron2 };
                // Step 3: Move hanging jobs.
                for (int i = 0; i < instance.hangingTakeoffTasks.Count; i++)
                {
                    TakeoffTask hangingTask = instance.hangingTakeoffTasks[i];
                    Apron virtualApron = virtualAprons[i];

                    virtualApron.isOccupied = true;
                    hangingTask.apron = virtualApron;
                    hangingTask.gameObject.transform.SetParent(virtualApron.transform);
                    hangingTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);
                }

                // Step 4: Hide emtpy virtual aprons.
                for (int i = instance.hangingTakeoffTasks.Count; i < virtualAprons.Length; i++)
                {
                    Apron virtualApron = virtualAprons[i];
                    virtualApron.isOccupied = false;
                    virtualApron.GetComponentInChildren<Image>().fillAmount = 0f;
                }
            }

            return false;
        }
    }

    public static class ReflectionExtensions
    {
        public static T GetFieldValue<T>(this object obj, string name)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            return (T)field?.GetValue(obj);
        }

        public static void SetFieldValue<T>(this object obj, string name, T value)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            field.SetValue(obj, value);
        }
    }
}
