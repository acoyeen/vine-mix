// WPF PNGs use two pixels per native logical DIP.
export const ACTIVITY_DURATION_MS = 900;
export const NODE_TRANSIT_MS = ACTIVITY_DURATION_MS;
export const CABLE_TRANSIT_MS = 700;
export const nodeTransitTime = () => NODE_TRANSIT_MS;
export const nodeStartTime = index => index*(NODE_TRANSIT_MS+CABLE_TRANSIT_MS);
// This is the exact horizontal-tangent Bezier in GraphWorkspace.Cable.
export function nativeCable(a, b) {
  const reach = Math.max(70, Math.abs(b.x - a.x) * .48);
  return `M${a.x},${a.y} C${a.x + reach},${a.y} ${b.x - reach},${b.y} ${b.x},${b.y}`;
}

export function connectNodes(poses) {
  const port = (node, output) => {
    const p = node.ports.find(p => p.output === output);
    return {x:node.x + p.x * node.scale, y:node.y + p.y * node.scale};
  };
  return poses.slice(0,-1).map((node,i) => {
    const a = port(node,true), b = port(poses[i+1],false);
    return {from:i,to:i+1,a,b,d:nativeCable(a,b),duration:CABLE_TRANSIT_MS};
  });
}

export function layoutNodes(nodes) {
  const scale = .5;
  let x = 50;
  const poses = nodes.map(node => {
    const pose = {...node,x,y:90,w:node.width*scale,h:node.height*scale,scale};
    x += pose.w + 90;
    return pose;
  });
  const width = x - 90 + 50;
  const height = Math.max(...poses.map(p=>p.h)) + 200;
  return {poses,wires:connectNodes(poses),width,height};
}
