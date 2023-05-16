// 从 https://vpm.directus.app/assets/936917f9-c2d4-4470-a5bc-9bb38176fa01?download&cache={DateTime.Now} 读取最新的 vrc-official.json
// 执行路径在 UpdateCheck 文件夹里，Packages 文件夹在上一级目录里
import { VRCOfficial } from "./types.ts";
import {
  decompress
} from "https://deno.land/x/zip@v1.2.3/mod.ts";
(async () => {
  // 格式：11/7/2022 5:00:52 AM
  const res = await fetch(`https://vrchat.github.io/packages/index.json?download&cache=${new Date().toLocaleString()}`);
  const json: VRCOfficial = await res.json()
  // 读取 com.vrchat.base 的版本号，位于 packages → com.vrchat.base → versions → x.x.x ，其中 x.x.x 为版本号，取最新的版本
  // Object.keys 选最后一个，即最新的版本
  const downloadList: [
    "com.vrchat.base",
    "com.vrchat.worlds",
    "com.vrchat.avatars"
  ] = [
      "com.vrchat.base",
      "com.vrchat.worlds",
      "com.vrchat.avatars"
    ]
  let hascopy = false
  for (const packageName of downloadList) {
    let baseVersionKeys = Object.keys(json.packages[packageName].versions)
    baseVersionKeys = (f => f(f(baseVersionKeys, 1).sort(), -1))((baseVersionKeys: any[], v: number) => baseVersionKeys.map(a => a.replace(/\d+/g, (n: string | number) => +n + v * 100000)))
    let baseVersion = baseVersionKeys.pop() ?? ""
    if (baseVersion.trim() === "" || baseVersion === undefined || baseVersion === null) {
      console.error("can't find base version")
      // Deno.exit(1)
      continue
    }
    let finded = false
    // 如果版本号不是 x.x.x 的格式，就继续取下一个
    while (!/^\d+\.\d+\.\d+$/.test(baseVersion)) {
      baseVersion = baseVersionKeys.pop() ?? ""
      if (baseVersion === "" || baseVersion === undefined || baseVersion === null) {
        console.error("can't find base version")
        // Deno.exit(1)
        break
      }
      finded = true
    }
    if (!finded) continue
    console.log(baseVersion)
    // 读取 version.txt 的内容，与 baseVersion 比较
    const versionTxt = await Deno.readTextFile(`${packageName}_version.txt`)
    if (versionTxt !== baseVersion) {
      // 如果不一致，则从 url 下载最新的 zip
      const version = baseVersion
      const url = json.packages[packageName].versions[version].url
      const res = await fetch(url)
      const zip = await res.arrayBuffer()
      // 解压到 VRChat/packageName 文件夹
      await Deno.mkdir("VRChat", { recursive: true })
      await Deno.writeFile(`VRChat/${packageName}.zip`, new Uint8Array(zip))
      // 解压 zip
      await decompress(`VRChat/${packageName}.zip`, `VRChat/${packageName}_latest`)
      // 删除 ../Packages/packageName
      await Deno.remove(`../Packages/${packageName}`, { recursive: true })
      // 将 VRChat/packageName_latest 移动到 ../Packages/packageName
      await Deno.rename(`VRChat/${packageName}_latest`, `../Packages/${packageName}`)
      // 写入 version.txt
      await Deno.writeTextFile(`${packageName}_version.txt`, baseVersion)
      // 删除 VRChat 文件夹
      await Deno.remove("VRChat", { recursive: true })
      // 读取 ../README.md ，替换版本号 versionTxt 为 baseVersion
      const readme = hascopy ? await Deno.readTextFile("../README.md") : await Deno.readTextFile("rt.md")
      await Deno.writeTextFile("../README.md", readme.replaceAll(`__packageName__`, `${baseVersion}`))
      const readmez = hascopy ? await Deno.readTextFile("../README_zh.md") : await Deno.readTextFile("rtzh.md")
      await Deno.writeTextFile("../README_zh.md", readmez.replaceAll(`__packageName__`, `${baseVersion}`))
      hascopy = true
    }
  }
})()
