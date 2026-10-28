// Scroll poses for unchanged native WPF artwork, with original transparent padding.
export const sceneSize = { width: 1280, height: 820 };
export const nodeWidth = 240;
export const clamp = value => Math.max(0, Math.min(1, value));
const ease = value => { const t = clamp(value); return t * t * (3 - 2 * t); };
const range = (value, from, to) => ease((value - from) / (to - from));
const lerp = (a, b, t) => a + (b - a) * t;
export const geometry = {
  mic: { width:784,height:786,out:[747,130] },
  listen: { width:784,height:714,in:[37,578] },
  share: { width:784,height:714,in:[37,578] },
  tone: { width:824,height:1118,in:[37,886],out:[787,130] },
  music: { width:784,height:714,out:[747,130] },
  duck: { width:784,height:1184,in:[37,896],trigger:[37,952],out:[747,130] },
};
const basic = {
  mic:[100,350,1], listen:[780,50,1], share:[1000,500,1],
  tone:[330,410,0], music:[330,60,0], duck:[700,60,0],
};
const entry = {
  ...basic,
  mic:[250,350,1], listen:[700,160,1], share:[700,460,1],
};
const effects = {
  mic:[10,380,1], listen:[780,50,1], share:[1000,500,1],
  tone:[330,380,1], music:[330,60,0], duck:[700,60,0],
};
export function storyFrame(rawProgress) {
  const progress=clamp(rawProgress), arrive=range(progress,0,.14), insert=range(progress,.20,.36);
  const poses={};
  for(const id of Object.keys(basic)) poses[id]=basic[id].map((v,k)=>lerp(lerp(entry[id][k],v,arrive),effects[id][k],insert));
  poses.tone[2]=range(progress,.25,.36);
  // Staggered starting columns keep both outputs clear while they take a
  // single diagonal, with the same gentle acceleration and settling period.
  const relocate=range(progress,.62,.80);
  poses.listen[0]=lerp(poses.listen[0],700,relocate);
  poses.listen[1]=lerp(poses.listen[1],580,relocate);
  poses.share[0]=lerp(poses.share[0],1040,relocate);
  poses.share[1]=lerp(poses.share[1],350,relocate);
  for(const id of ['music','duck']) {
    poses[id][1]=lerp(60,30,range(progress,.80,.86));
    poses[id][2]=range(progress,.80,.86);
  }
  const port=(id,kind)=>geometry[id][kind].map((v,k)=>poses[id][k]+v*nodeWidth/geometry[id].width);
  const wires=[];
  const add=(id,from,to,opacity,draw,kind='audio',lane=null)=>{
    const middle=lane??(from[0]+to[0])*.5;
    wires.push({id,kind,opacity,draw,d:'M'+from.join(' ')+' C'+[middle,from[1],middle,to[1],...to].join(' ')});
  };
  const basicOpacity=1-range(progress,.20,.24);
  // Let the existing output routes fade gently before either output moves.
  // This exit is slightly longer than the opening routes' fade.
  const effectsOpacity=range(progress,.36,.40)*(1-range(progress,.56,.62));
  const duckOpacity=range(progress,.86,.91);
  const entryDraw=range(progress,.025,.14);
  const effectInputDraw=range(progress,.36,.43), effectOutputDraw=range(progress,.385,.47);
  const finalDraw=range(progress,.86,.96), musicOutputDraw=range(progress,.88,.97);
  add('basic-listen',port('mic','out'),port('listen','in'),basicOpacity,entryDraw);
  add('basic-share',port('mic','out'),port('share','in'),basicOpacity,entryDraw);
  add('effect-in',port('mic','out'),port('tone','in'),range(progress,.36,.40),effectInputDraw);
  add('effect-listen',port('tone','out'),port('listen','in'),effectsOpacity,effectOutputDraw);
  add('effect-share',port('tone','out'),port('share','in'),effectsOpacity,effectOutputDraw);
  add('tone-listen',port('tone','out'),port('listen','in'),duckOpacity,finalDraw,'audio',635);
  add('trigger',port('tone','out'),port('duck','trigger'),duckOpacity,finalDraw,'trigger',635);
  add('voice-share',port('tone','out'),port('share','in'),duckOpacity,finalDraw,'audio',805);
  add('music-in',port('music','out'),port('duck','in'),duckOpacity,finalDraw,'audio',635);
  add('duck-share',port('duck','out'),port('share','in'),duckOpacity,musicOutputDraw,'audio',990);
  const toneAmount=range(progress,.40,.57);
  return { progress,poses,wires,chapter:progress<.26?0:progress<.68?1:2,toneAmount,
    tones:{low:3*toneAmount,mid:-2*toneAmount,high:2*toneAmount} };
}
