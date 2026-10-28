const NS = 'http://www.w3.org/2000/svg';
const make = (tag, attrs = {}) => {
  const el = document.createElementNS(NS,tag);
  for (const [key,value] of Object.entries(attrs)) el.setAttribute(key,value);
  return el;
};
const clamp = x => Math.max(0,Math.min(1,x));
export const treatments = [
  {id:'sheen',name:'Surface sheen',note:'A fine satin highlight passes diagonally through the panel.',tag:'Soft / continuous'},
  {id:'edge',name:'Edge relay',note:'Light travels along the inset edges, keeping the controls clear.',tag:'Quiet / structural'},
  {id:'ripple',name:'Pressure ripple',note:'Three soft wavefronts spread from the input and dissipate at the output.',tag:'Organic / expanding'},
  {id:'traces',name:'Twin traces',note:'Two fine signal trails carry the stereo channels through the node.',tag:'Precise / directional'},
  {id:'dots',name:'Dotted wave',note:'A band of tiny lights passes through a recessed dot field.',tag:'Textured / digital'},
  {id:'panels',name:'Panel sequence',note:'Five inset sections briefly brighten in order from input to output.',tag:'Rhythmic / stepped'},
  {id:'sockets',name:'Socket handoff',note:'The input acknowledges the signal; the output answers. The meters tell the story between.',tag:'Minimal / focused'},
  {id:'meters',name:'Meters only',note:'No sweep. Stereo meters and the EQ spectrum alone reveal the signal passing through.',tag:'Native / restrained'}
];

export function createNodeEffect(node, id) {
  const {width:w,height:h} = node;
  const input = node.ports.find(p=>!p.output) || {x:37,y:h-140};
  const output = node.ports.find(p=>p.output) || {x:w-37,y:130};
  const svg = make('svg',{class:'node-flow',viewBox:`0 0 ${w} ${h}`,'aria-hidden':'true'});
  const defs = make('defs');
  const clip = make('clipPath',{id:`${id}-clip`,clipPathUnits:'userSpaceOnUse'});
  clip.append(make('rect',{x:39,y:104,width:w-78,height:h-143,rx:6}));
  const gradient = make('radialGradient',{id,gradientUnits:'userSpaceOnUse',cx:0,cy:0,r:1});
  for (const [offset,opacity] of [[0,.55],[.2,.38],[.55,.13],[1,0]]) gradient.append(make('stop',{offset,'stop-color':'#b8e6ce','stop-opacity':opacity}));
  defs.append(clip,gradient);
  const root = make('g',{'clip-path':`url(#${id}-clip)`});
  const groups = {};
  for (const {id:kind} of treatments) { groups[kind]=make('g'); groups[kind].style.display='none';root.append(groups[kind]); }
  const rect = attrs => make('rect',{x:39,y:104,width:w-78,height:h-143,...attrs});
  groups.sheen.append(rect({fill:`url(#${id})`,opacity:.6}));
  groups.edge.append(make('rect',{x:46,y:111,width:w-92,height:h-157,rx:10,fill:'none',stroke:`url(#${id})`,'stroke-width':9}));
  const rings = [0,1,2].map(()=>{const circle=make('circle',{cx:input.x,cy:input.y,r:0,fill:'none',stroke:'#a5dfbf','stroke-width':4});groups.ripple.append(circle);return circle;});
  const dx=output.x-input.x,dy=output.y-input.y,distance=Math.hypot(dx,dy);
  const nx=-dy/distance,ny=dx/distance;
  for (const side of [-1,1]) {
    const x1=input.x+nx*11*side,y1=input.y+ny*11*side,x2=output.x+nx*11*side,y2=output.y+ny*11*side;
    const d=`M${x1} ${y1} C${x1+w*.32} ${y1} ${x2-w*.32} ${y2} ${x2} ${y2}`;
    groups.traces.append(make('path',{d,fill:'none',stroke:`url(#${id})`,'stroke-width':side===1?7:4,'stroke-linecap':'round'}));
  }
  const pattern = make('pattern',{id:`${id}-dots`,patternUnits:'userSpaceOnUse',width:26,height:26});
  pattern.append(make('circle',{cx:13,cy:13,r:2.7,fill:'white'}));
  const mask = make('mask',{id:`${id}-mask`,maskUnits:'userSpaceOnUse',x:0,y:0,width:w,height:h});
  mask.append(rect({fill:`url(#${id}-dots)`}));defs.append(pattern,mask);
  groups.dots.append(rect({fill:`url(#${id})`,mask:`url(#${id}-mask)`}));
  const panels = Array.from({length:5},(_,i)=>{
    const panel=make('rect',{x:44+i*(w-88)/5,y:108,width:(w-88)/5-8,height:h-152,rx:5,fill:'#a4dbba'});
    groups.panels.append(panel);return panel;
  });
  const sockets = [input,output].map(p=>{
    const socket=make('circle',{cx:p.x,cy:p.y,r:24,fill:'none',stroke:'#ccffe0','stroke-width':7});groups.sockets.append(socket);return socket;
  });
  svg.append(defs,root);
  return {svg,root,groups,gradient,rings,panels,sockets,input,output,width:w,height:h,distance,dx,dy};
}

export function paintNodeEffect(light, time, start, duration, kind='sockets') {
  const t=(time-start)/duration;
  const visibleKind = kind === 'sockets' || (t >= -.17 && t <= 1.17 && light.groups[kind]) ? kind : null;
  // The selected treatment is normally unchanged for the whole page visit.
  // Keep animating it without hiding/showing all eight groups on every frame.
  if (light.visibleKind !== visibleKind) {
    light.visibleKind = visibleKind;
    for (const [name, group] of Object.entries(light.groups)) group.style.display = name === visibleKind ? '' : 'none';
  }
  if(kind==='sockets') {
    // Keep the existing arrival times, but let each socket finish its own
    // release instead of cutting it off at the end of the node transit.
    const smooth=x=>{const t=clamp(x);return t*t*(3-2*t);};
    light.root.style.opacity=1;
    light.sockets.forEach((socket,i)=>{
      // Output begins its 32 ms rise only after the node's activity is finished.
      const peak=i ? duration+32 : Math.min(duration*.1,16);
      const age=time-start-peak;
      const strength=age<0?smooth((age+32)/32):1-smooth(age/380);
      socket.style.opacity=strength;
      socket.setAttribute('r',18+(1-strength)*18);
    });
    return;
  }
  if (!visibleKind) return;
  const p=clamp(t),fade=Math.min(clamp((t+.17)/.17),clamp((1.17-t)/.17));
  light.root.style.opacity=fade;
  const x=light.input.x+light.dx*p,y=light.input.y+light.dy*p,angle=Math.atan2(light.dy,light.dx)*180/Math.PI;
  let rx=light.width*.065,ry=light.distance*.7;
  if(kind==='edge') {rx=light.width*.2;ry=light.distance;}
  if(kind==='traces') {rx=light.width*.24;ry=light.width*.3;}
  if(kind==='dots') {rx=light.width*.18;ry=light.distance*.8;}
  light.gradient.setAttribute('gradientTransform',`translate(${x} ${y}) rotate(${angle}) scale(${rx} ${ry})`);
  light.rings.forEach((ring,i)=>{
    const age=p-i*.13;
    ring.setAttribute('r',Math.max(0,age)*light.distance*1.16);
    ring.style.opacity=age>0 ? (1-p)*.4 : 0;
  });
  light.panels.forEach((panel,i)=>{const delta=(p*6-i)/1.6;panel.style.opacity=delta>0&&delta<1?Math.sin(delta*Math.PI)*.14:0;});
}
