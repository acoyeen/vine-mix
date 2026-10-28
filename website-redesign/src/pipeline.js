import { layoutNodes, nodeTransitTime, ACTIVITY_DURATION_MS } from './pipeline-layout.js';
import { createNodeEffect, paintNodeEffect } from './pipeline-effects.js';
import { pipelineShell } from './pipeline-shell.js';

// Condensed from native node help where available; plain summaries for endpoints.
const descriptions = {
  microphone:'Brings audio in from your microphone.',
  highPass:'Reduces sound below the cutoff.',
  denoiser:'Reduces background noise for clearer speech.',
  expander:'Reduces quieter passages below the threshold.',
  compressor:'Reduces louder passages for a steadier level.',
  eq:'Shapes the bass, mids and highs.',
  virtualOutput:'Sends processed audio to your apps.'
};
const NS = 'http://www.w3.org/2000/svg';
let instanceCount = 0;
const smooth = t => { const x = Math.max(0,Math.min(1,t)); return x*x*(3-2*x); };
const activityEnvelope = age => smooth(age/100)*(1-smooth((age-(ACTIVITY_DURATION_MS-250))/250));
// Seeded, interpolated random values stay stable when paused or scrubbing.
const randomValue = (seed,x) => {
  const at=i=>{const n=Math.sin(i*127.1+seed*311.7)*43758.5453;return n-Math.floor(n);};
  const i=Math.floor(x),blend=smooth(x-i);
  return at(i)*(1-blend)+at(i+1)*blend;
};
const element = (name, attrs) => {
  const el = document.createElementNS(NS, name);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  return el;
};

// The cable stays intact. Light changes its inner colour through a smooth
// radial falloff, with no separate dot, dash caps or glow outside its edges.
function lightPath(svg, d, id, className) {
  const defs = element('defs', {});
  const gradient = element('radialGradient', {id,gradientUnits:'userSpaceOnUse',r:80});
  for (const [offset, opacity] of [[0,1],[.2,.95],[.48,.52],[.76,.12],[1,0]]) {
    gradient.append(element('stop', {offset,'stop-color':'#e5ffed','stop-opacity':opacity}));
  }
  defs.append(gradient);
  const path = element('path', {d,class:className,stroke:`url(#${id})`});
  svg.append(defs,path);
  return {path,gradient,length:path.getTotalLength()};
}

class VineMixPipeline extends HTMLElement {
  constructor() { super(); if (!this.shadowRoot) this.attachShadow({mode:'open'}); this.lightId = `pipeline-light-${++instanceCount}`; this.activitySeed=Math.random()*1000; }
  async connectedCallback() {
    if (this.initialized) { this.observe(); return; }
    this.initialized = true;
    // Font faces are document resources; @font-face inside a shadow tree is
    // ignored by Chromium. Register namespaced local faces once per document.
    if (![...document.fonts].some(face => face.family.replaceAll('"', '') === 'Pipeline Plex')) {
      for (const [weight, file] of [['400','Regular'],['600','SemiBold']]) {
        const face = new FontFace('Pipeline Plex', `url("${new URL(`./assets/IBM-Plex-Sans-${file}.ttf`, import.meta.url)}")`, {weight,display:'swap'});
        document.fonts.add(face); face.load().catch(() => {});
      }
    }
    this.elapsed = 0;
    this.visible = false;
    if (!this.shadowRoot.querySelector('.stage') || !this.shadowRoot.querySelector('svg')) this.shadowRoot.innerHTML = pipelineShell();
    this.stage = this.shadowRoot.querySelector('.stage');
    this.svg = this.shadowRoot.querySelector('svg');
    this.stage.replaceChildren(this.svg);
    this.motion = matchMedia('(prefers-reduced-motion: reduce)');
    this.contrast = matchMedia('(forced-colors: active)');
    this.abort = new AbortController();
    const signal = this.abort.signal;
    const scroller = this.shadowRoot.querySelector('.canvas-scroll');
    scroller.addEventListener('keydown', event => {
      if (event.target !== scroller || !['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
      event.preventDefault();
      const left = event.key === 'Home' ? 0 : event.key === 'End' ? scroller.scrollWidth : scroller.scrollLeft + (event.key === 'ArrowRight' ? 260 : -260);
      scroller.scrollTo({left,behavior:'instant'});
    }, {signal});
    this.changeMotion = () => {
      if (this.motion.matches || this.contrast.matches) this.clearPulse();
      this.sync();
    };
    this.motion.addEventListener('change', this.changeMotion, {signal});
    this.contrast.addEventListener('change', this.changeMotion, {signal});
    document.addEventListener('visibilitychange', () => this.sync(), {signal});
    try {
      const response = await fetch(new URL('./assets/pipeline/nodes/nodes.json', import.meta.url), {signal});
      if (!response.ok) throw new Error('Native artwork unavailable');
      const nodes = await response.json();
      // A completed response can still settle after disconnect/reconnect.
      // Only the connection that requested it may build or replace this scene.
      if (signal.aborted || !this.isConnected) return;
      this.nodes = nodes;
      this.nodeElements = this.nodes.map((node, i) => {
        const card = document.createElement('figure');
        card.className = 'node';
        const image = new Image(); image.src = new URL(`./assets/pipeline/nodes/${node.file}`, import.meta.url); image.alt = '';
        image.width = node.width; image.height = node.height; image.draggable = false;
        const caption = document.createElement('figcaption'); caption.className = 'node-caption';
        const title = document.createElement('h3'); title.textContent = node.label;
        const description = document.createElement('p'); description.textContent = descriptions[node.kind];
        caption.append(title,description);
        card.append(caption,image);
        if (node.meter) {
          node.meter.layers = node.meter.lanes.map(() => {
            const layer = new Image(); layer.className = 'stereo-fill'; layer.alt = '';
            layer.src = new URL(`./assets/pipeline/nodes/${node.meter.file}`,import.meta.url);
            layer.setAttribute('aria-hidden','true');
            Object.assign(layer.style,{left:`${node.meter.x/node.width*100}%`,top:`${node.meter.y/node.height*100}%`,
              width:`${node.meter.width/node.width*100}%`,height:`${node.meter.height/node.height*100}%`,clipPath:'inset(100% 0 0 0)'});
            card.append(layer); return layer;
          });
        }
        this.stage.append(card); return card;
      });
      this.nodeLights = this.nodes.map((node,i) => {
        const effect = createNodeEffect(node,`${this.lightId}-node-${i}`);
        this.nodeElements[i].append(effect.svg);
        if (node.kind === 'eq') {
          // Plot bounds in the bundled 192 DPI native artwork, excluding axes.
          const plot = {x:222,y:306,width:944,height:456};
          const spectrum = element('svg',{class:'eq-spectrum',viewBox:'0 0 944 456','aria-hidden':'true'});
          Object.assign(spectrum.style,{left:`${plot.x/node.width*100}%`,top:`${plot.y/node.height*100}%`,width:`${plot.width/node.width*100}%`,height:`${plot.height/node.height*100}%`});
          const paths = [0,1].map(channel => {
            const path = element('path',{class:`spectrum-channel channel-${channel}`});
            spectrum.append(path); return path;
          });
          this.nodeElements[i].append(spectrum); this.eqSpectrum = {spectrum,paths};
        }
        return effect;
      });
      this.resizeObserver = new ResizeObserver(() => {
        if (!signal.aborted && this.isConnected) this.layout();
      });
      this.resizeObserver.observe(this);
      this.layout();
      this.observe();
      this.changeMotion();
      this.dispatchEvent(new Event('pipeline-ready'));
    } catch (error) {
      if (signal.aborted || !this.isConnected || error.name === 'AbortError') return;
      this.stage.innerHTML = '<p>Physical input → High-pass filter → Denoiser → Expander → Compressor → Equaliser → Virtual output</p><p>Node artwork could not load. Refresh to try again.</p>';
    }
  }
  observe() {
    this.observer?.disconnect();
    const signal = this.abort.signal;
    this.observer = new IntersectionObserver(([entry]) => {
      if (signal.aborted || !this.isConnected) return;
      this.visible = entry.isIntersecting;
      this.sync();
    });
    this.observer.observe(this);
  }
  layout(reset = false) {
    if (this.scene && reset !== true) { this.sizeWires(); return; }
    this.scene = layoutNodes(this.nodes);
    const {width, height, poses, wires} = this.scene;
    this.stage.style.aspectRatio = `${width}/${height}`;
    this.svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    this.svg.replaceChildren();
    poses.forEach((p, i) => Object.assign(this.nodeElements[i].style, {left:`${p.x/width*100}%`,top:`${p.y/height*100}%`,width:`${p.w/width*100}%`,height:`${p.h/height*100}%`}));
    this.wires = wires.map(wire => {
      const outline = element('path', {d:wire.d, class:'wire-outline','vector-effect':'non-scaling-stroke'});
      const path = element('path', {d:wire.d, class:'wire','vector-effect':'non-scaling-stroke'});
      this.svg.append(outline,path);
      const light = lightPath(this.svg,wire.d,`${this.lightId}-wire-${wire.from}`,'pulse');
      return {...wire,outline,path,pulse:light.path,gradient:light.gradient,length:light.length};
    });
    this.totalDuration = this.wires.reduce((sum, wire) => sum + wire.duration, this.nodes.reduce((sum,_,i)=>sum+nodeTransitTime(i),450));
    this.elapsed = 0;
    this.paint(0);
    this.sizeWires();
  }
  sizeWires() {
    if (!this.wires) return;
    const zoom = this.stage.getBoundingClientRect().width / this.scene.width;
    // A hidden host has no usable scale; its next visible resize will size it.
    if (!(zoom > 0) || !Number.isFinite(zoom)) return;
    // Native SetWireWidth: 2.5 world DIPs, with a 1.6 screen-pixel minimum.
    const stroke = Math.max(2.5 * zoom, 1.6);
    this.wires.forEach(w => {
      w.path.style.strokeWidth = stroke;
      w.outline.style.strokeWidth = stroke + 2;
      w.pulse.style.strokeWidth = stroke * .72 / zoom;
      w.gradient.setAttribute('r',34 / zoom);
    });
  }
  clearPulse() {
    this.wires?.forEach(w => { w.pulse.style.opacity = 0; });
    this.nodeLights?.forEach(n => { n.root.style.opacity = 0; });
    if (this.eqSpectrum) this.eqSpectrum.spectrum.style.opacity = 0;
    this.nodes?.forEach(n => n.meter?.layers?.forEach(layer => { layer.style.clipPath = 'inset(100% 0 0 0)'; }));
  }
  paintNode(light, time, start, duration) {
    paintNodeEffect(light,time,start,duration,this.treatment || 'sockets');
  }
  paintMeter(node, time, start) {
    const meter = node.meter;
    if (!meter?.layers) return;
    meter.layers.forEach((layer,channel) => {
      // A sustained speech-like phrase, sharing timing with the spectrum.
      const age = (time-start+this.totalDuration)%this.totalDuration;
      const envelope = activityEnvelope(age);
      const peakDb = channel === 0 ? -8 : -11;
      const movement=.95+.05*randomValue(this.activitySeed+channel,age/130);
      const fill = envelope*movement*(60+peakDb)/60;
      const lane = meter.lanes[channel];
      const top = (lane.y+lane.height*(1-fill))/meter.height*100;
      const right = (meter.width-lane.x-lane.width)/meter.width*100;
      const bottom = (meter.height-lane.y-lane.height)/meter.height*100;
      layer.style.clipPath = `inset(${top}% ${right}% ${bottom}% ${lane.x/meter.width*100}%)`;
    });
  }
  paintSpectrum(time, start) {
    if (!this.eqSpectrum) return;
    const age = (time-start+this.totalDuration)%this.totalDuration;
    const envelope = activityEnvelope(age);
    if (envelope <= 0) return;
    this.eqSpectrum.spectrum.style.opacity = envelope;
    this.eqSpectrum.paths.forEach((path,channel) => {
      const points = [];
      const seed=this.activitySeed+channel*.37;
      // Voice-range energy with independently moving, softly rounded formants.
      // Preserve the visual peak height while redistributing the old sub-bass.
      const shift=(randomValue(seed+4,age/310)-.5)*.03;
      const temporalNoise=k=>randomValue(seed+k*7.13,age/155);
      for (let bin=0; bin<=96; bin++) {
        const u = bin/96;
        const body=.57*Math.exp(-Math.pow((u-.34-shift)/.13,2));
        const mids=.4*Math.exp(-Math.pow((u-.5)/.13,2));
        const presence=.32*Math.exp(-Math.pow((u-.71+shift)/.13,2));
        const k=Math.floor(u*24),blend=smooth(u*24-k);
        const variation=temporalNoise(k)*(1-blend)+temporalNoise(k+1)*blend;
        const texture=1+.28*(variation-.5)+.055*Math.sin(u*Math.PI*30+age*.002);
        const edge=smooth((u-.15)/.15)*smooth((1-u)/.13);
        points.push({x:u*944,level:(body+mids+presence)*edge*texture});
      }
      const highest=Math.max(...points.map(p=>p.level));
      const peak=.84*(.96+.04*randomValue(seed+9,age/220))*(channel ? .89 : 1);
      points.forEach(p=>{p.y=456-envelope*peak*p.level/highest*456;});
      let d=`M0,456 L0,${points[0].y.toFixed(2)}`;
      for(let i=0;i<points.length-1;i++) {
        const a=points[i],b=points[i+1];
        d+=` Q${a.x.toFixed(2)},${a.y.toFixed(2)} ${((a.x+b.x)/2).toFixed(2)},${((a.y+b.y)/2).toFixed(2)}`;
      }
      path.setAttribute('d',`${d} T944,456 Z`);
    });
  }
  paintLight(light, time, start) {
    const t = (time-start)/light.duration;
    // Every node must finish both channels before its outgoing cable lights up.
    if (time < start) return;
    // Brief overlap carries the soft leading/trailing light across the socket.
    const overlap = .035;
    if(t < -overlap || t > 1+overlap) return;
    const route = light.pulse;
    const length = light.length;
    const at = Math.max(0,Math.min(1,t))*length;
    const point = route.getPointAtLength(at);
    const opacity = Math.max(0,Math.min(1,(time-start)/32,(1+overlap-t)/overlap));
    light.gradient.setAttribute('cx',point.x);
    light.gradient.setAttribute('cy',point.y);
    route.style.opacity = opacity;
  }
  paint(time) {
    this.clearPulse();
    if(this.motion.matches || this.contrast.matches) return;
    let offset = 0;
    for(let i=0;i<this.nodes.length;i++) {
      const duration = nodeTransitTime(i);
      this.paintNode(this.nodeLights[i],time,offset,duration);
      this.paintMeter(this.nodes[i],time,offset);
      if (this.nodes[i].kind === 'eq') this.paintSpectrum(time,offset);
      offset += duration;
      if(!this.wires[i]) continue;
      this.paintLight(this.wires[i],time,offset);
      offset += this.wires[i].duration;
    }
  }
  sync() {
    cancelAnimationFrame(this.frame); this.frame = null; this.previousTime = null;
    if (!this.wires || !this.isConnected || !this.visible || document.hidden || this.motion.matches || this.contrast.matches || this.paused) return;
    const tick = now => {
      if (!this.isConnected || !this.visible || document.hidden || this.motion.matches || this.contrast.matches || this.paused) {
        this.sync(); return;
      }
      const start=this.rangeStart || 0,end=this.rangeEnd || this.totalDuration;
      if (this.previousTime != null) this.elapsed = start + (this.elapsed-start + Math.min(now - this.previousTime, 100)*(this.playbackSpeed || 1)) % (end-start);
      this.previousTime = now; this.paint(this.elapsed);
      this.dispatchEvent(new CustomEvent('pipeline-frame',{detail:this.elapsed}));
      this.frame = requestAnimationFrame(tick);
    };
    this.frame = requestAnimationFrame(tick);
  }
  disconnectedCallback() {
    cancelAnimationFrame(this.frame); this.observer?.disconnect(); this.resizeObserver?.disconnect();
    this.abort?.abort(); this.initialized = false;
    this.scene = null; this.wires = null; this.nodeElements = null; this.nodeLights = null; this.eqSpectrum = null;
  }
}
customElements.define('vine-mix-pipeline', VineMixPipeline);
