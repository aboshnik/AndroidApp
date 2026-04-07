package com.example.app

import android.content.Intent
import android.net.Uri
import android.webkit.MimeTypeMap
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.MediaController
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.VideoView
import androidx.appcompat.app.AlertDialog
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.request.CachePolicy
import com.google.android.material.imageview.ShapeableImageView
import android.widget.FrameLayout
import com.example.app.api.PollOptionItem
import com.example.app.api.PostItem
import com.example.app.utils.DateTimeFormatUtils

class FeedAdapter(
    private val canManagePosts: Boolean,
    private val onDeletePost: (PostItem) -> Unit,
    private val onVote: (postId: Int, optionId: Int, onDone: (com.example.app.api.PollItem?) -> Unit) -> Unit
) : ListAdapter<PostItem, FeedAdapter.ViewHolder>(DiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_feed_post, parent, false)
        return ViewHolder(view, canManagePosts, onDeletePost, onVote)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    class ViewHolder(
        itemView: View,
        private val canManagePosts: Boolean,
        private val onDeletePost: (PostItem) -> Unit,
        private val onVote: (postId: Int, optionId: Int, onDone: (com.example.app.api.PollItem?) -> Unit) -> Unit
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
            if (post.poll != null) {
                more.text = "Опрос / Подробнее"
                more.visibility = View.VISIBLE
            } else {
                more.text = "Подробнее"
                more.visibility = if (isLongText) View.VISIBLE else View.GONE
            }
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
            val detailsPollTitle = dialogView.findViewById<TextView>(R.id.detailsPollTitle)
            val detailsPollStatus = dialogView.findViewById<TextView>(R.id.detailsPollStatus)
            val detailsPollOptions = dialogView.findViewById<LinearLayout>(R.id.detailsPollOptions)
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
            var currentPoll = post.poll
            fun renderPoll() {
                bindPoll(post.id, currentPoll, detailsPollTitle, detailsPollStatus, detailsPollOptions) { optionId ->
                    onVote(post.id, optionId) { updated ->
                        if (updated != null) {
                            currentPoll = updated
                            renderPoll()
                        }
                    }
                }
            }
            renderPoll()

            val dialog = AlertDialog.Builder(itemView.context)
                .setView(dialogView)
                .create()

            btnClose.setOnClickListener { dialog.dismiss() }
            dialog.setOnDismissListener { runCatching { detailsVideo.stopPlayback() } }
            dialog.show()
        }

        private fun bindPoll(
            postId: Int,
            poll: com.example.app.api.PollItem?,
            titleView: TextView,
            statusView: TextView,
            optionsContainer: LinearLayout,
            onOptionClick: (Int) -> Unit
        ) {
            if (poll == null) {
                titleView.visibility = View.GONE
                statusView.visibility = View.GONE
                optionsContainer.visibility = View.GONE
                return
            }

            titleView.visibility = View.VISIBLE
            titleView.text = poll.question
            statusView.visibility = View.VISIBLE
            statusView.text = buildString {
                append("Голосов: ${poll.totalVotes}. ")
                append(
                    if (poll.canViewResults) {
                        if (poll.hasVoted) "Вы уже проголосовали." else "Можно голосовать."
                    } else {
                        "Результаты скрыты до окончания опроса."
                    }
                )
            }
            optionsContainer.visibility = View.VISIBLE
            optionsContainer.removeAllViews()
            poll.options.forEach { option ->
                optionsContainer.addView(
                    buildOptionView(
                        inflater = LayoutInflater.from(itemView.context),
                        canViewResults = poll.canViewResults,
                        showVoters = poll.showVoters,
                        selectedOptionId = poll.selectedOptionId,
                        option = option,
                        totalVotes = poll.totalVotes,
                        onOptionClick = onOptionClick
                    )
                )
            }
        }

        private fun buildOptionView(
            inflater: LayoutInflater,
            canViewResults: Boolean,
            showVoters: Boolean,
            selectedOptionId: Int?,
            option: PollOptionItem,
            totalVotes: Int,
            onOptionClick: (Int) -> Unit
        ): View {
            val v = inflater.inflate(R.layout.item_poll_option, null, false)
            val tvCheck = v.findViewById<TextView>(R.id.tvOptionCheck)
            val tvLeft = v.findViewById<TextView>(R.id.tvOptionLeft)
            val tvRight = v.findViewById<TextView>(R.id.tvOptionRight)
            val pb = v.findViewById<ProgressBar>(R.id.pbOption)
            val llVoters = v.findViewById<LinearLayout>(R.id.llVoters)

            val isSelected = selectedOptionId == option.id
            tvCheck.visibility = if (isSelected) View.VISIBLE else View.GONE
            val percentInt = if (canViewResults && totalVotes > 0) {
                ((option.votesCount.toFloat() / totalVotes.toFloat()) * 100f).toInt().coerceIn(0, 100)
            } else 0
            val hasAnyVotes = canViewResults && totalVotes > 0
            // Telegram-like: show percent only after first vote exists
            tvLeft.text = if (hasAnyVotes) "${percentInt}% ${option.text}" else option.text
            tvRight.text = if (hasAnyVotes) option.votesCount.toString() else ""

            val votersText = if (canViewResults && showVoters && !option.voters.isNullOrEmpty()) {
                " [${option.voters.joinToString()}]"
            } else {
                ""
            }
            if (votersText.isNotBlank()) {
                tvLeft.text = "${tvLeft.text}$votersText"
            }

            // Hide bar until results visible and votes exist
            pb.visibility = if (hasAnyVotes) View.VISIBLE else View.GONE
            pb.progress = if (hasAnyVotes) percentInt else 0

            val votersList = option.voters.orEmpty()
            if (showVoters && hasAnyVotes && votersList.isNotEmpty()) {
                llVoters.visibility = View.VISIBLE
                llVoters.removeAllViews()
                val maxAvatars = 6
                val shown = votersList.take(maxAvatars)
                shown.forEach { voter ->
                    val root = inflater.inflate(R.layout.item_poll_voter_avatar, llVoters, false) as FrameLayout
                    val iv = root.findViewById<ShapeableImageView>(R.id.ivVoterAvatar)
                    val tv = root.findViewById<TextView>(R.id.tvVoterInitial)

                    val initial = voter.login.trim().firstOrNull()?.uppercaseChar()?.toString() ?: "?"
                    tv.text = initial

                    val url = voter.avatarUrl?.trim().orEmpty()
                    if (url.isBlank()) {
                        iv.visibility = View.GONE
                        tv.visibility = View.VISIBLE
                    } else {
                        iv.visibility = View.VISIBLE
                        tv.visibility = View.GONE
                        iv.load(url) {
                            placeholder(R.drawable.ic_launcher_simple)
                            error(R.drawable.ic_launcher_simple)
                            listener(
                                onError = { _, _ ->
                                    iv.visibility = View.GONE
                                    tv.visibility = View.VISIBLE
                                }
                            )
                        }
                    }

                    llVoters.addView(root)
                }
                val remaining = votersList.size - shown.size
                if (remaining > 0) {
                    val more = TextView(itemView.context).apply {
                        text = "+$remaining"
                        setTextColor(0xFF666666.toInt())
                        textSize = 12f
                    }
                    llVoters.addView(more)
                }
            } else {
                llVoters.visibility = View.GONE
                llVoters.removeAllViews()
            }

            val lp = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { topMargin = 8 }
            v.layoutParams = lp
            v.setOnClickListener { onOptionClick(option.id) }
            return v
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
