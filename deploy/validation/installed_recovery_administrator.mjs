// A second genuine login, without sharing cookies or replacing the owner.
import assert from 'node:assert/strict'
import { loginVisible } from './installed_ui_permission_session.mjs'
import { logoutFromUserMenu } from './installed_ui_logout.mjs'

export async function withRecoveryAdministrator(args, perform) {
  const { context: ownerContext, base, api, route, workspace, state, record, save, recoveryAdministrator, authorityUsername } = args
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_ADMINISTRATOR_PROBE, '1')
  assert.equal(process.env.WHALEDECK_INSTALLED_RETIREMENT_AUTHORITY_PROBE, undefined)
  assert.equal(recoveryAdministrator.role, 'administrator')
  assert.match(recoveryAdministrator.username, /^whaledeck-platform-ui-[a-f0-9]{32}$/)
  assert.notEqual(recoveryAdministrator.username, authorityUsername)
  assert.equal(workspace.id, state.workspace.id)
  assert.equal(workspace.slug, state.slug)
  assert.equal(workspace.ownerSubject, state.ownerSubject)
  record.recovery ??= {}
  const proof = record.recovery.administrator ??= {}
  const browser = ownerContext.browser()
  assert.ok(browser)
  // newContext receives no storageState, cookies, tokens or session injection.
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'light', reducedMotion: 'reduce' })
  context.setDefaultTimeout(30_000)
  const page = await context.newPage()
  let loggedIn = false, result
  try {
    const profile = await loginVisible(page, context, base, `${route}/${workspace.id}/environment`, true, recoveryAdministrator)
    loggedIn = true
    assert.match(profile.subject, /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/)
    assert.notEqual(profile.subject, state.ownerSubject)
    proof.subject ??= profile.subject
    assert.equal(proof.subject, profile.subject, 'RECOVERY_ADMINISTRATOR_CHANGED')
    proof.ownerSubject = state.ownerSubject
    proof.independentVisibleLogin = true
    const response = await context.request.get(`${base}${api}/${workspace.id}`)
    assert.equal(response.status(), 200)
    const selected = await response.json()
    assert.equal(selected.id, workspace.id)
    assert.equal(selected.ownerSubject, state.ownerSubject)
    proof.resourceOwnerPreserved = true
    await save()
    result = await perform({ ...args, context, page })
    assert.equal(result.state, 'Succeeded')
    const current = await context.request.get(base+'/api/v1/auth/me')
    assert.equal(current.status(), 200)
    assert.equal((await current.json()).subject, proof.subject)
    proof.recoveryCompletedByIndependentSession = true
    await save()
  } finally {
    try {
      if (loggedIn) {
        proof.logout = await logoutFromUserMenu(page, context, base)
        await save()
      }
    } finally { await context.close() }
  }
  // Signing out the second identity must preserve the original owner's login.
  const owner = await ownerContext.request.get(base+'/api/v1/auth/me')
  assert.equal(owner.status(), 200)
  assert.equal((await owner.json()).subject, state.ownerSubject)
  proof.originalOwnerSessionPreserved = true
  proof.passed = true
  await save()
  return result
}
