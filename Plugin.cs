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

            // Grab sprites from AF1 events.
            PreLevel02Manager preLevel02Manager = (PreLevel02Manager)GameObject.FindObjectOfType(typeof(PreLevel02Manager));
            if (preLevel02Manager == null)
            {
                return;
            }

            List<MapSelection.MapItem> MapData = preLevel02Manager.GetFieldValue<List<MapSelection.MapItem>>("MapData");
            if (MapData == null)
            {
                return;
            }
            foreach (MapSelection.MapItem mapDatum in MapData)
            {
                if (mapDatum.sceneName == "SanFrancisco")
                {
                    AirForceOne af1 = mapDatum.MapContent.GetComponentInChildren<AirForceOne>();
                    if (af1 == null)
                    {
                        return;
                    }
                    GameObject F16Prefab = af1.GetFieldValue<GameObject>("F16Prefab");
                    GameObject B747Prefab = af1.GetFieldValue<GameObject>("B747Prefab");
                    if (F16Prefab == null || B747Prefab == null)
                    {
                        return;
                    }

                    GameObject F16Obj = UnityEngine.Object.Instantiate<GameObject>(F16Prefab, new Vector3(10, 10, 0), Quaternion.identity);
                    Aircraft F16 = F16Obj.GetComponent<Aircraft>();
                    F16Sprite = F16.AP.GetComponent<SpriteRenderer>()?.sprite;
                    F16.ConditionalDestroy();

                    GameObject B747Obj = UnityEngine.Object.Instantiate<GameObject>(B747Prefab, new Vector3(-10, -10, 0), Quaternion.identity);
                    Aircraft B747 = B747Obj.GetComponent<Aircraft>();
                    B747Sprite = B747.AP.GetComponent<SpriteRenderer>()?.sprite;
                    B747.ConditionalDestroy();
                    break;
                }
            }
        }

        public static Manager Instance;
        public static int score = 0;
        public static Sprite F16Sprite;
        public static Sprite B747Sprite;
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

            yield return new WaitForSeconds(PLANE_DOWN_TIME);

            // Resume normal speed.
            friendly.aircraft_.speed = friendly.GetTargetSpeed();

            // Enemy down.
            AircraftDown(enemy);
            Manager.AddScore();

            if (--friendly.ammo_ <= 0)
            {
                // Time to head back.
                Vector3 position = friendly.aircraft_.AP.transform.position;
                float heading = friendly.aircraft_.heading;

                friendly.aircraft_.ConditionalDestroy();

                Aircraft newAircraft = AircraftManager.Instance.CreateInboundAircraft(position, heading);

                // Larger plane icon.
                newAircraft.AP.gameObject.transform.localScale *= 1.5f;

                // Change the sprite of newAircraft.
                SpriteRenderer sr = newAircraft.AP.GetComponent<SpriteRenderer>();
                if (sr != null && Manager.F16Sprite != null)
                {
                    sr.sprite = Manager.F16Sprite;
                }

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

        public static void AircraftDown(AircraftTag tag)
        {
            if (tag == null)
            {
                Plugin.Log.LogInfo("tag is null!");
                return;
            }
            tag.down_ = true;
            Manager.Instance.StartCoroutine(AircraftDownCoroutine(tag));
        }

        public float GetTargetSpeed()
        {
            switch(aircraft_.colorCode)
            {
                case ColorCode.Option.LightBlue:
                    return 28f;
                case ColorCode.Option.Green:
                    return 32f;
                case ColorCode.Option.Pink:
                    return 36f;
                case ColorCode.Option.Yellow:
                    return 40f;
                case ColorCode.Option.Orange:
                    return 44f;
                case ColorCode.Option.Red:
                    return 48f;
            }
            return 36f;
        }

        public float GetTurnSpeed()
        {
            switch(aircraft_.colorCode)
            {
                case ColorCode.Option.LightBlue:
                    return 0.09f;
                case ColorCode.Option.Green:
                    return 0.08f;
                case ColorCode.Option.Pink:
                    return 0.07f;
                case ColorCode.Option.Yellow:
                    return 0.06f;
                case ColorCode.Option.Orange:
                    return 0.055f;
                case ColorCode.Option.Red:
                    return 0.05f;
            }
            return 0.06f;
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

        private static IEnumerator AircraftDownCoroutine(AircraftTag tag)
        {
            Color APColor = tag.aircraft_.AP.GetComponent<Renderer>().material.color;
            Color PanelColor = tag.aircraft_.Panel.GetComponent<Renderer>().material.color;
            Vector2 APScale = tag.aircraft_.AP.transform.localScale;
            Vector2 PanelScale = tag.aircraft_.AP.transform.localScale;

            float t = 0;
            float maxRotation = 360f;
            float startTime = Time.time;
            while (Time.time < startTime + PLANE_DOWN_TIME && tag.aircraft_ != null)
            {
                tag.aircraft_.AP.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, maxRotation, t));
                tag.aircraft_.AP.transform.localScale = new Vector2(Mathf.Lerp(APScale.x, 0f, t), Mathf.Lerp(APScale.y, 0f, t));
                tag.aircraft_.AP.GetComponent<Renderer>().material.color = new Color(APColor.r, APColor.g, APColor.b, Mathf.Lerp(1f, 0f, t));

                tag.aircraft_.Panel.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, maxRotation, t));
                tag.aircraft_.Panel.transform.localScale = new Vector2(Mathf.Lerp(PanelScale.x, 0f, t), Mathf.Lerp(PanelScale.y, 0f, t));
                tag.aircraft_.Panel.GetComponent<Renderer>().material.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, Mathf.Lerp(1f, 0f, t));

                t += Time.deltaTime / PLANE_DOWN_TIME;

                yield return new WaitForFixedUpdate();
            }

            tag.aircraft_.ConditionalDestroy();
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

        public bool friendly_ = true;
        public Aircraft aircraft_;
        public int ammo_ = 2;
        public IEnumerator activeCoroutine_ = null;
        public bool down_ = false;
        private const float SEARCH_STEP_GRADIENT = 0.1f;
        private const float MAX_SEARCH_DISTANCE = 4f;
        private const float DISTANCE_THRESHOLD = 0.6f;
        private const float PLANE_DOWN_TIME = 3f;
    }

    // Add friendly tag to Aircraft, and slow down incoming aircraft.
    [HarmonyPatch(typeof(Aircraft), "Start", new Type[] {})]
    class PatchAircraftStart
    {
        static void Postfix(ref Aircraft __instance, ref float ___acceleration)
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

                    // Larger plane icon.
                    __instance.AP.gameObject.transform.localScale *= 1.5f;

                    // Change the sprite of planes.
                    SpriteRenderer sr = __instance.AP.GetComponent<SpriteRenderer>();
                    if (sr == null || Manager.B747Sprite == null)
                    {
                        return;
                    }
                    sr.sprite = Manager.B747Sprite;
                } else {
                    // Change the sprite of planes.
                    SpriteRenderer sr = __instance.AP.GetComponent<SpriteRenderer>();
                    if (sr == null || Manager.F16Sprite == null)
                    {
                        return;
                    }
                    sr.sprite = Manager.F16Sprite;
                }

            }
            ___acceleration = 0.04f;
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
                        !tag2.friendly_ && tag2.aircraft_.direction == Aircraft.Direction.Inbound && !tag2.down_ &&
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
                        !tag2.friendly_ && tag2.aircraft_.direction == Aircraft.Direction.Inbound && !tag2.down_ &&
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

            // Friendly collides with enemy without shooting it down.
            if (tag1.friendly_ != tag2.friendly_ && tag1.friendly_ || tag2.friendly_ && !tag1.down_ && !tag2.down_)
            {
                if (!Manager.IsSubstractScoreGameOver())
                {
                    AircraftTag.AircraftDown(tag1);
                    AircraftTag.AircraftDown(tag2);
                    return false;
                }
                return true;
            }

            // Friendly collides.
            if (tag1.friendly_ && tag2.friendly_ && !tag1.down_ && !tag2.down_)
            {
                // Two friendly down.
                Manager.score--;
                if (!Manager.IsSubstractScoreGameOver())
                {
                    AircraftTag.AircraftDown(tag1);
                    AircraftTag.AircraftDown(tag2);
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
            // Attach Manager to ESC button.
            GameObject esc_button = GameObject.Find("ESC_Button");
            esc_button.gameObject.AddComponent<Manager>();

            if (MapManager.gameMode == GameMode.SandBox)
            {
                return;
            }

            // Max-out all airspace.
            Camera.main.DOOrthoSize(LevelManager.Instance.maximumCameraOrthographicSize, 0.5f).SetUpdate(isIndependentUpdate: true);
        }
    }

    // Remove not useful upgrades.
    [HarmonyPatch(typeof(UpgradeManager), "ProcessOptionProbs", new Type[] {})]
    class PatchProcessOptionProbs
    {
        static void Postfix(ref List<float> __result)
        {
            __result[3] = 0; // TURN_FASTER
            __result[4] = 0; // AIRSPACE
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

    // Patch turning radius.
    [HarmonyPatch(typeof(Aircraft), "TrySetupLanding", new Type[] {typeof(Runway), typeof(bool)})]
    class PatchTrySetupLanding
    {
        static bool Prefix(Runway runway, bool doLand, ref Aircraft __instance, ref object[] __state)
        {
            
            AircraftTag tag = __instance.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return true;
            }

            __state = new object[] {Aircraft.TurnSpeed};
            Aircraft.TurnSpeed = tag.GetTurnSpeed();
            return true;
        }

        static void Postfix(Runway runway, bool doLand, ref Aircraft __instance, ref object[] __state)
        {
            if (__state != null && __state.Length > 0)
            {
                // Restore the global turning speed.
                Aircraft.TurnSpeed = (float)__state[0];
            }
        }
    }

    // Patching turning speed.
    [HarmonyPatch(typeof(Aircraft), "UpdateHeading", new Type[] {})]
    class PatchUpdateHeading
    {
        static bool Prefix(ref Aircraft __instance, ref PlaceableWaypoint ____HARWCurWP, ref object[] __state)
        {
            AircraftTag tag = __instance.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return true;
            }

            __state = new object[] {Aircraft.TurnSpeed};
            Aircraft.TurnSpeed = tag.GetTurnSpeed();
            return true;
        }

        static void Postfix(ref Aircraft __instance, ref PlaceableWaypoint ____HARWCurWP, ref object[] __state)
        {
            if (__state != null && __state.Length > 0)
            {
                // Restore the global turning speed.
                Aircraft.TurnSpeed = (float)__state[0];
            }
        }
    }

    // Patching turning speed.
    [HarmonyPatch(typeof(Aircraft), "GenerateFlyingPath", new Type[] {typeof(int)})]
    class PatchGenerateFlyingPath
    {
        static bool Prefix(int count, ref Aircraft __instance, ref object[] __state)
        {
            AircraftTag tag = __instance.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return true;
            }

            __state = new object[] {Aircraft.TurnSpeed};
            Aircraft.TurnSpeed = tag.GetTurnSpeed();
            return true;
        }

        static void Postfix(ref Aircraft __instance, ref object[] __state)
        {
            if (__state != null && __state.Length > 0)
            {
                // Restore the global turning speed.
                Aircraft.TurnSpeed = (float)__state[0];
            }
        }
    }

    // Patching turning speed.
    [HarmonyPatch(typeof(Aircraft), "PredictPosAfterTurn", new Type[] {typeof(float)})]
    class PatchPredictPosAfterTurn
    {
        static bool Prefix(float angle, ref Aircraft __instance, ref object[] __state)
        {
            AircraftTag tag = __instance.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return true;
            }

            __state = new object[] {Aircraft.TurnSpeed};
            Aircraft.TurnSpeed = tag.GetTurnSpeed();
            return true;
        }

        static void Postfix(float angle, ref Aircraft __instance, ref object[] __state)
        {
            if (__state != null && __state.Length > 0)
            {
                // Restore the global turning speed.
                Aircraft.TurnSpeed = (float)__state[0];
            }
        }
    }

    // Patching turning speed.
    [HarmonyPatch(typeof(Aircraft), "TurningRadius", MethodType.Getter)]
    class PatchTurningRadius
    {
        static void Postfix(ref Aircraft __instance, ref float __result)
        {
            AircraftTag tag = __instance.GetComponent<AircraftTag>();
            if (tag == null)
            {
                return;
            }

            float num = 360f / tag.GetTurnSpeed() * __instance.speed * Aircraft.SpeedScale;
            if (num == 0f)
            {
                __result = 0f;
            }
            __result = num / ((float)Math.PI * 2f);
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
            AircraftTag tag = aircraft.GetComponent<AircraftTag>();
            if (tag == null || tag.down_)
            {
                return true;
            }

            if (!Manager.IsSubstractScoreGameOver())
            {

                AircraftTag.AircraftDown(tag);
                return false;
            }
            return true;
        }
    }

    // Skip original point calculation.
    [HarmonyPatch(typeof(GameDataWhiteBoard), "OnAircraftHandOff", new Type[] {})]
    class PatchOnAircraftHandOff
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
        static bool Prefix(GameDataWhiteBoard __instance, ref int ____handOffCount)
        {
            ____handOffCount = Manager.score;
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
