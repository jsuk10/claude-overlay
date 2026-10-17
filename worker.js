// 정적 자산 앞에서 도는 Worker. Cloudflare는 *.html 주소를 확장자 없는 주소로 307 리디렉션하는데,
// 검색엔진 소유확인 파일은 리디렉션 없이 그 주소 그대로 200으로 응답해야 해서 여기서 직접 내준다.
const VERIFY = {
  '/navere1a45637919a913c1c5a85a55a159df4.html': 'naver-site-verification: navere1a45637919a913c1c5a85a55a159df4.html',
};

export default {
  async fetch(request, env) {
    const body = VERIFY[new URL(request.url).pathname];
    if (body) return new Response(body, { headers: { 'content-type': 'text/html; charset=utf-8' } });
    return env.ASSETS.fetch(request);
  },
};
