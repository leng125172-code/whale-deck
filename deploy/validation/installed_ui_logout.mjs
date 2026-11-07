// Visible production controls only. No session injection or auth token export.
import assert from 'node:assert/strict'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function logoutFromUserMenu(page, context, base, dialog, observe=()=>{}) {
  let phase='startup',bffStatus=0,identityStatus=0
  async function mark(value,extra={}){phase=value;await observe({phase:value,...extra})}
  try{
    if(dialog&&await dialog.isVisible())await dialog.locator('.el-dialog__headerbtn').click()
    await mark('started')
    const navigation=page.getByRole('button',{name:'打开导航',exact:true})
    const sidebar=page.locator('.sidebar')
    const user=page.getByRole('button',{name:'打开用户菜单',exact:true})
    await expect.poll(async()=>await user.isVisible()||await navigation.isVisible()).toBe(true)
    if(await navigation.isVisible()&&!await sidebar.evaluate(element=>element.classList.contains('sidebar--mobile-open')))await navigation.click()
    await expect(user).toBeInViewport();await mark('controls-ready');await user.click();await mark('menu-opened')
    const origin=new URL(base), provider=new URL(base);provider.port='8081'
    // The SPA can transiently show /login before the top-level OIDC navigation.
    // Observe that actual navigation and final server session states, not the transient route.
    // Attach rejection handlers immediately: a missing earlier navigation must
    // not leave another navigation wait as an unhandled rejection.
    const observed=promise=>promise.then(value=>({value}),error=>({error}))
    const required=async promise=>{const result=await promise;if(result.error)throw result.error;return result.value}
    const mainNavigation=request=>request.isNavigationRequest()&&request.frame()===page.mainFrame()
    const posted=observed(page.waitForResponse(response=>mainNavigation(response.request())&&response.request().method()==='POST'&&new URL(response.url()).origin===origin.origin&&new URL(response.url()).pathname==='/api/v1/auth/logout'))
    const reachedProvider=observed(page.waitForRequest(request=>mainNavigation(request)&&new URL(request.url()).origin===provider.origin&&new URL(request.url()).pathname.endsWith('/end-session/')))
    const returnedToRp=observed(page.waitForResponse(response=>{
      const url=new URL(response.url())
      return mainNavigation(response.request())&&url.origin===origin.origin&&['/signout-callback-oidc','/signout-callback-oidc/internal','/signout-callback-oidc/external'].includes(url.pathname)&&[302,303].includes(response.status())
    }))
    await page.getByRole('menuitem',{name:'退出登录',exact:true}).click()
    const response=await required(posted);assert.ok([302,303].includes(response.status()));await mark('bff-accepted',{postStatus:response.status()});await required(reachedProvider);await mark('provider-observed')
    // BrowserContext.request shares the browser cookie jar. Do not query the
    // identity API while its invalidation flow is flushing/replacing sessions:
    // even a read can send a session-cookie deletion response. First observe
    // the real RP callback; this is a wait, never a manual navigation/fallback.
    await required(returnedToRp);await mark('rp-callback-observed')
    await expect(page).toHaveURL(url=>url.origin===origin.origin&&url.pathname==='/login',{timeout:30_000})
    await mark('login-route',{bffStatus,identityStatus})
    const identity=new URL(base);identity.port='8081';identity.pathname='/api/v3/core/users/me/';identity.search=''
    await expect.poll(async()=>{
      bffStatus=(await context.request.get(base+'/api/v1/auth/me')).status()
      identityStatus=(await context.request.get(identity.href)).status()
      return bffStatus===401&&[401,403].includes(identityStatus)
    },{timeout:30_000}).toBe(true)
    await mark('sessions-cleared',{bffStatus,identityStatus})
    if(dialog)await expect(dialog).toHaveCount(0)
    return {normalUiLogout:true,postStatus:response.status(),providerNavigationObserved:true,rpCallbackObserved:true,bffStatus,identityStatus,finalLoginPage:true}
  }catch(error){
    await observe({phase:'failed',atStep:phase,failureType:error?.name,bffStatus,identityStatus})
    throw error
  }
}
