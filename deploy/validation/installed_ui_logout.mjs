// Visible production controls only. No session injection or auth token export.
import assert from 'node:assert/strict'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function logoutFromUserMenu(page, context, base, dialog) {
  if(dialog&&await dialog.isVisible())await dialog.locator('.el-dialog__headerbtn').click()
  const navigation=page.getByRole('button',{name:'打开导航',exact:true})
  const sidebar=page.locator('.sidebar')
  const user=page.getByRole('button',{name:'打开用户菜单',exact:true})
  await expect.poll(async()=>await user.isVisible()||await navigation.isVisible()).toBe(true)
  if(await navigation.isVisible()&&!await sidebar.evaluate(element=>element.classList.contains('sidebar--mobile-open')))await navigation.click()
  await expect(user).toBeInViewport();await user.click()
  const origin=new URL(base), provider=new URL(base);provider.port='8081'
  // The SPA can transiently show /login before the top-level OIDC navigation.
  // Observe that actual navigation and final server session states, not the transient route.
  const posted=page.waitForResponse(response=>response.request().isNavigationRequest()&&response.request().method()==='POST'&&new URL(response.url()).origin===origin.origin&&new URL(response.url()).pathname==='/api/v1/auth/logout')
  const reachedProvider=page.waitForRequest(request=>request.isNavigationRequest()&&new URL(request.url()).origin===provider.origin&&new URL(request.url()).pathname.endsWith('/end-session/'))
  await page.getByRole('menuitem',{name:'退出登录',exact:true}).click()
  const response=await posted;assert.ok([302,303].includes(response.status()));await reachedProvider
  const identity=new URL(base);identity.port='8081';identity.pathname='/api/v3/core/users/me/';identity.search=''
  let bffStatus=0, identityStatus=0
  await expect.poll(async()=>{
    bffStatus=(await context.request.get(base+'/api/v1/auth/me')).status()
    identityStatus=(await context.request.get(identity.href)).status()
    return bffStatus===401&&[401,403].includes(identityStatus)
  },{timeout:30_000}).toBe(true)
  await expect(page).toHaveURL(url=>url.origin===origin.origin&&url.pathname==='/login',{timeout:30_000})
  if(dialog)await expect(dialog).toHaveCount(0)
  return {normalUiLogout:true,postStatus:response.status(),providerNavigationObserved:true,bffStatus,identityStatus,finalLoginPage:true}

}
