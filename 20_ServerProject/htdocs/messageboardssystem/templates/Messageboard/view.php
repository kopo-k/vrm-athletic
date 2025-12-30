<?php
/**
 * @var \App\View\AppView $this
 * @var \App\Model\Entity\Messageboard $messageboard
 */
?>
<div class="row">
    <aside class="column">
        <div class="side-nav">
            <h4 class="heading"><?= __('Actions') ?></h4>
            <?= $this->Html->link(__('Edit Messageboard'), ['action' => 'edit', $messageboard->id], ['class' => 'side-nav-item']) ?>
            <?= $this->Form->postLink(__('Delete Messageboard'), ['action' => 'delete', $messageboard->id], ['confirm' => __('Are you sure you want to delete # {0}?', $messageboard->id), 'class' => 'side-nav-item']) ?>
            <?= $this->Html->link(__('List Messageboard'), ['action' => 'index'], ['class' => 'side-nav-item']) ?>
            <?= $this->Html->link(__('New Messageboard'), ['action' => 'add'], ['class' => 'side-nav-item']) ?>
        </div>
    </aside>
    <div class="column column-80">
        <div class="messageboard view content">
            <h3><?= h($messageboard->Name) ?></h3>
            <table>
                <tr>
                    <th><?= __('Name') ?></th>
                    <td><?= h($messageboard->Name) ?></td>
                </tr>
                <tr>
                    <th><?= __('Message') ?></th>
                    <td><?= h($messageboard->Message) ?></td>
                </tr>
                <tr>
                    <th><?= __('Id') ?></th>
                    <td><?= $this->Number->format($messageboard->id) ?></td>
                </tr>
                <tr>
                    <th><?= __('Date') ?></th>
                    <td><?= h($messageboard->Date) ?></td>
                </tr>
            </table>
        </div>
    </div>
</div>