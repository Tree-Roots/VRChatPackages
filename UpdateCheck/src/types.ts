export interface VRCCurated {
  name: string
  author: string
  url: string
  packages: {
    [key in "com.vrchat.clientsim"
    | "com.vrchat.udonsharp"
    | "com.llealloo.audiolink"
    | "dev.onevr.vrworldtoolkit"
    | "vrchat.jordo.easyquestswitch"
    | "dev.vrlabs.av3manager"
    | "vrchat.blackstartx.gesture-manager"
    ]: {
      versions: VERSIONS
    }
  }
}
export interface VRCOfficial {
  name: string
  author: string
  url: string
  packages: {
    [key in "com.vrchat.base"
    | "com.vrchat.worlds"
    | "com.vrchat.avatars"
    | "com.vrchat.core.vpm-resolver"
    ]: {
      versions: VERSIONS
    }
  }
}
interface VERSIONS {
  [key: string]: {
    name: string
    displayName: string
    version: string
    unity: string
    description: string
    dependencies: {
      [key: string]: string
    }
    vpmDependencies: {
      [key: string]: string
    }
    samples: {
      displayName: string
      description: string
      path: string
    }[]
    author: {
      name: string
      email: string
      url: string
    }
    hideInEditor: boolean
    url: string
    repo: string
    legacyFolders: {
      [key: string]: string
    }
  }
}