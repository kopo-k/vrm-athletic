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
            <?= $this->Html->link(__('List Messageboard'), ['action' => 'index'], ['class' => 'side-nav-item']) ?>
        </div>
    </aside>
    <div class="column column-80">
        <div class="messageboard form content">
            <?= $this->Form->create($messageboard) ?>
            <fieldset>
                <legend><?= __('Add Messageboard') ?></legend>
                <?php
                    echo $this->Form->control('Name');
                    echo $this->Form->control('Message');
                    echo $this->Form->control('Date');
                ?>
            </fieldset>
            <?= $this->Form->button(__('Submit')) ?>
            <?= $this->Form->end() ?>
        </div>
    </div>
</div>
