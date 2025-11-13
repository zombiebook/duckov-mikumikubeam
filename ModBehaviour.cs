using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

using Duckov.Modding;

namespace mikumikubeam
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        public KeyCode FireKey = KeyCode.N;

        private LineRenderer _lr;
        private float _beamEnd;

        private const float BeamDuration = 0.08f;

        // 영상 관련
        private GameObject _videoCanvas;
        private VideoPlayer _videoPlayer;
        private RenderTexture _rt;
        private string _videoPath;

        private bool _videoPlaying = false;
        private bool _godmodeLocked = false;

        // 적 즉사를 위한 방문 체크
        private HashSet<GameObject> _visited = new HashSet<GameObject>();

        protected override void OnAfterSetup()
        {
            Debug.Log("[mikumikubeam] Mod Loaded!");

            string dllPath = Assembly.GetExecutingAssembly().Location;
            string folder = Path.GetDirectoryName(dllPath);

            _videoPath = Path.Combine(folder, "mikumikubeam.mp4");

            SetupBeam();
        }

        private void SetupBeam()
        {
            _lr = this.gameObject.AddComponent<LineRenderer>();
            _lr.positionCount = 2;
            _lr.startWidth = 0.06f;
            _lr.endWidth = 0.06f;
            _lr.material = new Material(Shader.Find("Sprites/Default"));
            _lr.startColor = Color.cyan;
            _lr.endColor = Color.cyan;
            _lr.enabled = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(FireKey))
            {
                FireBeam();
                PlayInternalVideo();
            }

            if (_lr.enabled && Time.time >= _beamEnd)
                _lr.enabled = false;
        }

        // ============================
        // 빔 (사운드 없음, 라인만)
        // ============================
        private void FireBeam()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 start = cam.transform.position;
            Vector3 end = start + cam.transform.forward * 60f;

            _lr.SetPosition(0, start);
            _lr.SetPosition(1, end);

            _lr.enabled = true;
            _beamEnd = Time.time + BeamDuration;
        }

        // ============================
        // 내부 영상 재생 (사운드 포함)
        // ============================
        private void PlayInternalVideo()
        {
            if (_videoPlaying) return;
            _videoPlaying = true;

            // Canvas 생성
            _videoCanvas = new GameObject("MikuBeamCanvas");
            var canvas = _videoCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99999;
            _videoCanvas.AddComponent<CanvasScaler>();
            _videoCanvas.AddComponent<GraphicRaycaster>();

            // RawImage
            GameObject rawObj = new GameObject("VideoRaw");
            rawObj.transform.SetParent(_videoCanvas.transform, false);
            var img = rawObj.AddComponent<RawImage>();
            img.raycastTarget = false;

            var rect = img.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // RenderTexture
            _rt = new RenderTexture(1920, 1080, 16);
            img.texture = _rt;

            // VideoPlayer
            _videoPlayer = _videoCanvas.AddComponent<VideoPlayer>();
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _rt;

            _videoPlayer.source = VideoSource.Url;
            string url = "file:///" + _videoPath.Replace("\\", "/");
            _videoPlayer.url = url;

            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;

            // 🔊 오디오를 AudioSource 모드로 사용
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.controlledAudioTrackCount = 1;

            // 카메라에 AudioSource 붙여서 영상 소리 출력
            AudioSource audioSrc = Camera.main.gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;   // 2D 사운드
            audioSrc.volume = 1.0f;

            _videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.SetTargetAudioSource(0, audioSrc);

            // 영상 종료 이벤트
            _videoPlayer.loopPointReached += OnVideoEnd;

            // 준비 완료되면 재생
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.Prepare();

            // 무적 시작
            ApplyGodmode(true);
            _godmodeLocked = true;

            Debug.Log("[mikumikubeam] 영상 준비 시작");
        }

        private void OnVideoPrepared(VideoPlayer vp)
        {
            Debug.Log("[mikumikubeam] 영상 준비 완료 → 재생 시작");
            vp.prepareCompleted -= OnVideoPrepared;
            vp.Play();
        }

        private void OnVideoEnd(VideoPlayer vp)
        {
            Debug.Log("[mikumikubeam] 영상 종료 → 전체 즉사 실행");

            ExecuteGlobalKill();

            GameObject.Destroy(_videoCanvas);
            GameObject.Destroy(_rt);

            _videoPlaying = false;

            StartCoroutine(DelayedGodmodeOff());
        }

        private IEnumerator DelayedGodmodeOff()
        {
            yield return new WaitForSeconds(3f);

            if (_godmodeLocked)
            {
                ApplyGodmode(false);
                _godmodeLocked = false;
                Debug.Log("[mikumikubeam] 3초 후 무적 해제");
            }
        }

        // ============================
        // 플레이어 판별 (절대 죽이면 안됨)
        // ============================
        private bool IsPlayerObject(GameObject obj)
        {
            if (obj == null) return true;

            if (CharacterMainControl.Main != null)
            {
                var main = CharacterMainControl.Main.gameObject;
                if (obj == main || obj.transform.IsChildOf(main.transform))
                    return true;
            }

            if (Camera.main != null)
            {
                if (obj.transform.IsChildOf(Camera.main.transform.root))
                    return true;
            }

            string n = obj.name.ToLower();
            if (n.Contains("player") || n.Contains("local"))
                return true;

            var hp = obj.GetComponentInParent<Health>();
            if (hp != null && hp.MaxHealth > 500)
                return true;

            return false;
        }

        // ============================
        // 전체 즉사
        // ============================
        private void ExecuteGlobalKill()
        {
            _visited.Clear();

            var all = GameObject.FindObjectsOfType<Component>();

            foreach (var comp in all)
            {
                if (comp == null) continue;

                GameObject obj = comp.gameObject;
                if (_visited.Contains(obj)) continue;

                _visited.Add(obj);

                if (IsPlayerObject(obj)) continue;

                TryKill(obj);
            }
        }

        private bool TryKill(GameObject obj)
        {
            try
            {
                var comps = obj.GetComponentsInParent<Component>(true);
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    Type t = c.GetType();

                    var hurt = t.GetMethod("Hurt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float) }, null);
                    if (hurt != null)
                    {
                        hurt.Invoke(c, new object[] { 999999f });
                        return true;
                    }

                    var hpProp = t.GetProperty("CurrentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (hpProp != null && hpProp.CanWrite)
                    {
                        hpProp.SetValue(c, 0f);
                        return true;
                    }

                    var kill = t.GetMethod("Kill", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (kill != null)
                    {
                        kill.Invoke(c, null);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // ============================
        // 무적 시스템 (Duckov 전용)
        // ============================
        private void ApplyGodmode(bool enabled)
        {
            try
            {
                var main = CharacterMainControl.Main;
                if (main == null) return;

                var hp = main.Health;
                if (hp == null) return;

                hp.SetInvincible(enabled);

                Debug.Log("[mikumikubeam] Godmode " + (enabled ? "ON" : "OFF"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[mikumikubeam] ApplyGodmode ERROR: " + ex.Message);
            }
        }
    }
}
