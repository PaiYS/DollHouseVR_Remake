using UnityEngine;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// The modern version of the 2016 webcam-on-the-ceiling trick.
    /// The bird's-eye client captures its webcam, compresses frames to JPEG and writes
    /// them into the datastore; every other client decodes them onto the ceiling,
    /// so the VR player who looks up sees the designer staring down at them.
    /// </summary>
    public class WebcamCeilingStream : RealtimeComponent<WebcamStreamModel> {
        [SerializeField] private Renderer _ceilingRenderer;
        [SerializeField] private int   _captureWidth    = 320;
        [SerializeField] private int   _captureHeight   = 240;
        [SerializeField] private float _framesPerSecond = 4f;
        [SerializeField, Range(1, 100)] private int _jpegQuality = 40;

        private WebCamTexture _webcamTexture;
        private Texture2D     _captureBuffer;
        private Texture2D     _receiveTexture;
        private float         _nextCaptureTime;
        private bool          _sending;

        /// <summary>Called by RoleManager on the bird's-eye client only.</summary>
        public void StartSending() {
            if (_sending) return;

            if (WebCamTexture.devices.Length == 0) {
                Debug.LogWarning("WebcamCeilingStream: no webcam found; ceiling stream disabled.");
                return;
            }

            _webcamTexture = new WebCamTexture(_captureWidth, _captureHeight, 15);
            _webcamTexture.Play();
            _sending = true;
        }

        protected override void OnRealtimeModelReplaced(WebcamStreamModel previousModel, WebcamStreamModel currentModel) {
            if (previousModel != null)
                previousModel.frameDataDidChange -= FrameDataDidChange;

            if (currentModel != null) {
                currentModel.frameDataDidChange += FrameDataDidChange;
                if (currentModel.frameData != null && currentModel.frameData.Length > 0)
                    ApplyFrame(currentModel.frameData);
            }
        }

        private void FrameDataDidChange(WebcamStreamModel model, byte[] frameData) {
            // The sender already sees its own webcam; only receivers decode.
            if (!_sending)
                ApplyFrame(frameData);
        }

        private void ApplyFrame(byte[] jpegData) {
            if (jpegData == null || jpegData.Length == 0 || _ceilingRenderer == null)
                return;

            if (_receiveTexture == null) {
                _receiveTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                _ceilingRenderer.material.mainTexture = _receiveTexture;
            }
            _receiveTexture.LoadImage(jpegData); // resizes automatically
        }

        private void Update() {
            if (!_sending || model == null || _webcamTexture == null)
                return;
            if (!_webcamTexture.didUpdateThisFrame || Time.time < _nextCaptureTime)
                return;

            _nextCaptureTime = Time.time + 1f / Mathf.Max(0.1f, _framesPerSecond);

            int w = _webcamTexture.width;
            int h = _webcamTexture.height;
            if (w < 16 || h < 16) return; // camera not ready yet

            if (_captureBuffer == null || _captureBuffer.width != w || _captureBuffer.height != h)
                _captureBuffer = new Texture2D(w, h, TextureFormat.RGB24, false);

            _captureBuffer.SetPixels32(_webcamTexture.GetPixels32());
            _captureBuffer.Apply(false);

            model.frameData = _captureBuffer.EncodeToJPG(_jpegQuality);
        }

        private void OnDestroy() {
            if (_webcamTexture != null)
                _webcamTexture.Stop();
        }
    }
}
