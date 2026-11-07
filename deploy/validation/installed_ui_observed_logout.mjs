// Bounded logout diagnostics: no token/state/query values, cookies or credentials.
import assert from 'node:assert/strict'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'
import { writeSignal } from './installed_ui_permission_session.mjs'

export async function observedLogout(label,page,context,base,evidence) {
  assert.match(label,/^(owner|recovery)-logout$/)
  const navigation=[]
  const requested=request=>{
    if(!request.isNavigationRequest()||navigation.length>=20)return
    const url=new URL(request.url())
    navigation.push({kind:'request',method:request.method(),origin:url.origin,path:url.pathname,hasIdTokenHint:url.searchParams.has('id_token_hint'),hasPostLogoutRedirect:url.searchParams.has('post_logout_redirect_uri')})
  }
  const responded=response=>{
    if(!response.request().isNavigationRequest()||navigation.length>=20)return
    const url=new URL(response.url());navigation.push({kind:'response',status:response.status(),origin:url.origin,path:url.pathname})
  }
  page.on('request',requested);page.on('response',responded)
  try {
    return await logoutFromUserMenu(page,context,base,undefined,async observation=>{
      const url=new URL(page.url());await writeSignal(evidence,label+'-'+observation.phase+'.json',{...observation,pageOrigin:url.origin,pagePath:url.pathname})
    })
  } finally {
    page.off('request',requested);page.off('response',responded);await writeSignal(evidence,label+'-navigation.json',{navigation})
  }
}
