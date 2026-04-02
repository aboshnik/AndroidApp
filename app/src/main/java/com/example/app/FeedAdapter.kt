package com.example.app

import android.content.Intent
import android.net.Uri
import android.webkit.MimeTypeMap
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.MediaController
import android.widget.TextView
import android.widget.VideoView
import androidx.appcompat.app.AlertDialog
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.request.CachePolicy
import com.example.app.api.PostItem
import com.example.app.utils.DateTimeFormatUtils

class FeedAdapter(
    private val canManagePosts: Boolean,
    private val onDeletePost: (PostItem) -> Unit
) : ListAdapter<PostItem, FeedAdapter.ViewHolder>(DiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_feed_post, parent, false)
        return ViewHolder(view, canManagePosts, onDeletePost)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    class ViewHolder(
        itemView: View,
        private val canManagePosts: Boolean,
        private val onDeletePost: (PostItem) -> Unit
    ) : RecyclerView.ViewHolder(itemView) {
        private val image: ImageView = itemView.findViewById(R.id.postImage)
        private val title: TextView = itemView.findViewById(R.id.postTitle)
        private val more: TextView = itemView.findViewById(R.id.postMore)
        private val delete: TextView = itemView.findViewById(R.id.postDelete)
        private val date: TextView = itemView.findViewById(R.id.postDate)

        fun bind(post: PostItem) {
            val url = post.imageUrl?.trim().orEmpty()
            if (url.isNotBlank()) {
                image.visibility = View.VISIBLE
                if (isVideoUrl(url)) {
                    image.setImageResource(android.R.drawable.ic_media_play)
                    image.scaleType = ImageView.ScaleType.CENTER
                    image.setBackgroundResource(R.drawable.bg_feed_card)
                } else {
                    image.scaleType = ImageView.ScaleType.CENTER_CROP
                    image.load(url) {
                        crossfade(true)
                        diskCachePolicy(CachePolicy.ENABLED)
                        memoryCachePolicy(CachePolicy.ENABLED)
                        placeholder(R.drawable.bg_feed_card)
                        error(android.R.drawable.ic_dialog_alert)
                        listener(
                            onError = { _, _ ->
                                // if load failed keep the gray background visible
                            }
                        )
                    }
                }
                image.setOnClickListener { openMedia(url) }
            } else {
                image.visibility = View.GONE
                image.setImageDrawable(null)
                image.setOnClickListener(null)
            }

            title.text = post.content
            val isLongText = post.content.length > 180
            more.visibility = if (isLongText) View.VISIBLE else View.GONE
            more.setOnClickListener {
                showDetailsDialog(post)
            }
            delete.visibility = if (canManagePosts) View.VISIBLE else View.GONE
            delete.setOnClickListener {
                AlertDialog.Builder(itemView.context)
                    .setTitle("Удалить новость")
                    .setMessage("Удалить эту новость?")
                    .setPositiveButton("Удалить") { _, _ -> onDeletePost(post) }
                    .setNegativeButton("Отмена", null)
                    .show()
            }
            date.text = DateTimeFormatUtils.formatRuDateTime(post.createdAt)
        }

        private fun showDetailsDialog(post: PostItem) {
            val dialogView = LayoutInflater.from(itemView.context)
                .inflate(R.layout.dialog_post_details, null, false)

            val detailsImage = dialogView.findViewById<ImageView>(R.id.detailsImage)
            val detailsVideo = dialogView.findViewById<VideoView>(R.id.detailsVideo)
            val detailsText = dialogView.findViewById<TextView>(R.id.detailsText)
            val btnClose = dialogView.findViewById<com.google.android.material.button.MaterialButton>(R.id.btnCloseDetails)
            val url = post.imageUrl?.trim().orEmpty()

            if (url.isNotBlank()) {
                if (isVideoUrl(url)) {
                    detailsImage.visibility = View.GONE
                    detailsVideo.visibility = View.VISIBLE
                    val mediaController = MediaController(itemView.context)
                    mediaController.setAnchorView(detailsVideo)
                    detailsVideo.setMediaController(mediaController)
                    detailsVideo.setVideoURI(Uri.parse(url))
                    detailsVideo.setOnPreparedListener { mp ->
                        mp.isLooping = false
                        detailsVideo.start()
                        mediaController.show()
                    }
                } else {
                    detailsVideo.visibility = View.GONE
                    detailsImage.visibility = View.VISIBLE
                    detailsImage.load(url) {
                        crossfade(true)
                        diskCachePolicy(CachePolicy.ENABLED)
                        memoryCachePolicy(CachePolicy.ENABLED)
                        placeholder(R.drawable.bg_feed_card)
                        error(android.R.drawable.ic_dialog_alert)
                    }
                    detailsImage.setOnClickListener { openMedia(url) }
                }
            } else {
                detailsImage.visibility = View.GONE
                detailsVideo.visibility = View.GONE
            }

            detailsText.text = post.content

            val dialog = AlertDialog.Builder(itemView.context)
                .setView(dialogView)
                .create()

            btnClose.setOnClickListener { dialog.dismiss() }
            dialog.setOnDismissListener { runCatching { detailsVideo.stopPlayback() } }
            dialog.show()
        }

        private fun openMedia(url: String) {
            val i = Intent(itemView.context, MediaViewerActivity::class.java)
            i.putExtra(MediaViewerActivity.EXTRA_URL, url)
            itemView.context.startActivity(i)
        }

        private fun isVideoUrl(url: String): Boolean {
            val path = Uri.parse(url).path?.lowercase().orEmpty()
            if (path.endsWith(".mp4") || path.endsWith(".mov") || path.endsWith(".webm")
                || path.endsWith(".mkv") || path.endsWith(".3gp") || path.endsWith(".m4v")
            ) return true

            val ext = MimeTypeMap.getFileExtensionFromUrl(url)?.lowercase().orEmpty()
            val mime = if (ext.isNotBlank()) MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) else null
            return mime?.startsWith("video/") == true
        }
    }

    private class DiffCallback : DiffUtil.ItemCallback<PostItem>() {
        override fun areItemsTheSame(a: PostItem, b: PostItem) = a.id == b.id
        override fun areContentsTheSame(a: PostItem, b: PostItem) = a == b
    }
}
