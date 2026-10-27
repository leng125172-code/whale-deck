// Read-only transport evidence; never retains URLs, headers, cookies or bodies.
export function observeUpload(session, path, initialEvent) {
  if (!/^\/api\/v1\/containers\/[a-f0-9]{64}\/files\/uploads\/[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\/content$/.test(path))
    throw new Error('invalid-upload-observer-path')
  let requestId,disposed=false
  const state={seen:false,terminal:null,networkError:null,canceled:false,responseStatus:null}
  const owns=event=>!disposed&&requestId!==undefined&&event.requestId===requestId
  const handlers={
    'Network.requestWillBeSent':event=>{
      if(disposed||requestId!==undefined||event.request.method!=='PUT')return
      try{if(new URL(event.request.url).pathname!==path)return}catch{return}
      requestId=event.requestId;state.seen=true
    },
    'Network.responseReceived':event=>{
      if(owns(event)&&!state.terminal)state.responseStatus=event.response.status
    },
    'Network.loadingFailed':event=>{
      if(!owns(event)||state.terminal)return
      state.terminal='failed';state.canceled=event.canceled===true
      state.networkError=/^net::ERR_[A-Z_]{1,64}$/.test(event.errorText)?event.errorText:'unclassified-network-error'
    },
    'Network.loadingFinished':event=>{
      if(owns(event)&&!state.terminal)state.terminal='finished'
    },
  }
  for(const [name,handler] of Object.entries(handlers))session.on(name,handler)
  // An actual CDP event may identify the assigned intent before the browser
  // registration response reaches the test process. Process that original
  // event through the same strict scope check; never invent a terminal event.
  if(initialEvent)handlers['Network.requestWillBeSent'](initialEvent)
  return {snapshot:()=>({...state}),dispose(){disposed=true;for(const [name,handler] of Object.entries(handlers))session.off(name,handler)}}
}
