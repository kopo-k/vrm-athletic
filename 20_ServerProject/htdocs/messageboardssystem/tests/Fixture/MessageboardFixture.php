<?php
declare(strict_types=1);

namespace App\Test\Fixture;

use Cake\TestSuite\Fixture\TestFixture;

/**
 * MessageboardFixture
 */
class MessageboardFixture extends TestFixture
{
    /**
     * Table name
     *
     * @var string
     */
    public string $table = 'messageboard';
    /**
     * Init method
     *
     * @return void
     */
    public function init(): void
    {
        $this->records = [
            [
                'id' => 1,
                'Name' => 'Lorem ipsum dolor sit amet',
                'Message' => 'Lorem ipsum dolor sit amet',
                'Date' => 1767069178,
            ],
        ];
        parent::init();
    }
}
