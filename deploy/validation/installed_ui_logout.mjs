// Visible production controls only. No session injection or auth token export.
import assert from 'node:assert/strict'
import { expect } from '../../web/node_modules/@playwright/test/index.mjs'

export async function logoutFromUserMenu(page, context, base, dialog) {
  if(dialog&&await dialog.isVisible())await dialog.locator('.el-dialog__headerbtn').click()
  const navigation=page.getByRole('button',{name:'打开导航',exact:true})
  const sidebar=page.locator('.sidebar')
  if(await navigation.isVisible()&&!await sidebar.evaluate(element=>element.classList.contains('sidebar--mobile-open')))await navigation.click()
  const user=page.getByRole('button',{name:'打开用户菜单',exact:true})
  await expect(user).toBeInViewport();await user.click()
  await page.getByRole('menuitem',{name:'退出登录',exact:true}).click()
  const origin=new URL(base)
  await expect(page).toHaveURL(url=>url.origin===origin.origin&&url.pathname==='/login',{timeout:30_000})
  if(dialog)await expect(dialog).toHaveCount(0)
  assert.equal((await context.request.get(base+'/api/v1/auth/me')).status(),401)
  const identity=new URL(base);identity.port='8081';identity.pathname='/api/v3/core/users/me/';identity.search=''
  assert.ok([401,403].includes((await context.request.get(identity.href)).status()))
}
