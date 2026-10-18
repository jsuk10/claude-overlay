// macOS로 접속하면 DMG 내려받기를 앞세운다.
// CPU를 알 수 있으면(Chrome·Edge의 userAgentData) 맞는 DMG를, 모르면(Safari) 그래픽 칩 이름으로 짐작하고, 그래도 모르면 Apple Silicon.
(function () {
  var ua = navigator.userAgentData;
  var platform = (ua && ua.platform) || navigator.platform || '';
  // iPad도 'MacIntel'로 보고하므로 터치 지점 수로 거른다
  if (!/mac/i.test(platform) || navigator.maxTouchPoints > 1) return;

  var mac = document.getElementById('dl-mac');
  var win = document.getElementById('dl-win');
  var note = document.getElementById('mac-note');
  if (!mac || !win) return;
  var base = 'https://github.com/jsuk10/claude-overlay/releases/latest/download/ClaudeSessionOverlay-macos-';

  function use(arch) {
    mac.href = base + arch + '.dmg';
    mac.textContent = mac.getAttribute(arch === 'x64' ? 'data-label-intel' : 'data-label-arm');
    mac.setAttribute('data-track', 'download_mac_' + arch);
  }

  function guess() {
    try {
      var gl = document.createElement('canvas').getContext('webgl');
      var info = gl && gl.getExtension('WEBGL_debug_renderer_info');
      var renderer = info ? String(gl.getParameter(info.UNMASKED_RENDERER_WEBGL)) : '';
      if (/Intel|AMD|Radeon|NVIDIA/i.test(renderer)) return 'x64';
    } catch (e) { /* WebGL이 꺼져 있다 */ }
    return 'arm64';
  }

  use(guess());
  mac.classList.add('primary');
  win.classList.remove('primary');
  mac.parentNode.insertBefore(mac, win);
  if (note) note.hidden = false;

  if (ua && ua.getHighEntropyValues) {
    ua.getHighEntropyValues(['architecture']).then(function (v) {
      if (v.architecture === 'arm') use('arm64');
      else if (v.architecture === 'x86') use('x64');
    }).catch(function () {});
  }
})();
