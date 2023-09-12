using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.Api;

namespace VRC.SDK3A.Editor
{
    /// <summary>
    /// This is the public interface you can use to interact with the Avatars SDK Builder
    /// </summary>
    public interface IVRCSdkAvatarBuilderApi: IVRCSdkBuilderApi
    {
        /// <summary>
        /// Builds the provided avatar GameObject and returns a path to the built avatar bundle.
        /// Make sure the avatar has a valid AvatarDescriptor and PipelineManager components attached.
        /// </summary>
        /// <param name="target">The avatar GameObject to build</param>
        /// <returns>Path to the bundle</returns>
        /// <exception cref="BuilderException">Build process has encountered an error</exception>
        /// <exception cref="BuildBlockedException">Build was blocked by the SDK Callback</exception>
        /// <exception cref="ValidationException">Content has validation errors</exception>
        Task<string> Build(GameObject target);
        
        /// <summary>
        /// Builds and uploads the provided avatar GameObject for the VRCAvatar specified.
        /// Make sure the avatar has a valid AvatarDescriptor and PipelineManager components attached
        /// </summary>
        /// <param name="target">The avatar GameObject to build</param>
        /// <param name="avatar">VRCAvatar object with avatar info. Must have a Name for avatar creation</param>
        /// <param name="thumbnailPath">Path to the thumbnail image on disk. Must be specified for first avatar creation</param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="BuilderException">Build process has encountered an error</exception>
        /// <exception cref="BuildBlockedException">Build was blocked by the SDK Callback</exception>
        /// <exception cref="ValidationException">Content has validation errors</exception>
        /// <exception cref="OwnershipException">Current User does not own the target content</exception>
        /// <exception cref="UploadException">Content failed to upload</exception>
        Task BuildAndUpload(GameObject target, VRCAvatar avatar, string thumbnailPath = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Builds the provided avatar GameObject and adds it to the test avatar list
        /// </summary>
        /// <param name="target">The avatar GameObject to build</param>
        /// <exception cref="BuilderException">Build process has encountered an error</exception>
        /// <exception cref="BuildBlockedException">Build was blocked by the SDK Callback</exception>
        /// <exception cref="ValidationException">Content has validation errors</exception>
        Task BuildAndTest(GameObject target);
    }
}